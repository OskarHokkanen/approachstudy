using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ApproachStudy
{
    /// <summary>Everything the logger needs to know about the trial being recorded.</summary>
    public struct TrialContext
    {
        public int participant;
        public int sequenceNumber;      // 1-based position in the session order
        public int sequenceLength;
        public int attempt;             // 1 = first try; goes up if a trial is aborted and redone
        public TrialDefinition def;
        public string modelName;
        public Vector3 calibPoint;      // calibrated spot, on the floor
        public Vector3 calibForward;    // direction the participant faced at calibration (flat)
        public Vector3 approachDir;     // flat unit vector from the participant towards where the character starts
        public float floorY;
    }

    /// <summary>
    /// Writes four files per session into Application.persistentDataPath/ApproachStudy/&lt;session&gt;/:
    ///   samples.csv  - head pose + character position every sample, for every trial
    ///   trials.csv   - one summary row per trial (displacement, retreat, side step, closest distance...)
    ///   events.csv   - calibrations, aborts, tracking resets...
    ///   session.txt  - participant, order, seed, settings
    ///
    /// Coordinate frames (all in metres, on the floor plane):
    ///   rel_right / rel_forward : head position relative to the calibrated spot, in the participant's calibrated
    ///                             facing direction (forward = where they faced, right = their right).
    ///   retreat / side          : movement since the start of this trial, relative to the character's approach.
    ///                             retreat + = moved away from the character, - = moved towards it.
    ///                             side + = moved to the participant's right (when facing the character), - = left.
    /// </summary>
    public class MotionLogger
    {
        public string SessionFolder { get; private set; }
        public bool SessionOpen => _samples != null;
        public bool TrialOpen { get; private set; }

        readonly string _sep;
        readonly bool _decimalComma;

        StreamWriter _samples, _trials, _events;
        float _sessionStartTime;

        // Per-trial state
        TrialContext _ctx;
        Vector3 _calibRight, _approachRight;
        bool _haveStart;
        Vector3 _startFlat;
        float _startYaw;
        int _sampleCount;
        float _lastT;
        float _maxDispStart, _tMaxDispStart, _maxDispCalib;
        float _maxRetreat, _maxAdvance, _maxRight, _maxLeft;
        float _finalRetreat, _finalSide;
        float _minCharDist, _tMinCharDist;
        float _maxAbsYawChange;

        public MotionLogger(bool europeanExcelFormat)
        {
            _decimalComma = europeanExcelFormat;
            _sep = europeanExcelFormat ? ";" : ",";
        }

        // ------------------------------------------------------------------ session

        public void BeginSession(int participant, string sessionInfoText)
        {
            EndSession();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            SessionFolder = Path.Combine(Application.persistentDataPath, "ApproachStudy", $"P{participant:000}_{stamp}");
            Directory.CreateDirectory(SessionFolder);
            _sessionStartTime = Time.realtimeSinceStartup;

            File.WriteAllText(Path.Combine(SessionFolder, "session.txt"), sessionInfoText, Encoding.UTF8);

            _samples = Open("samples.csv");
            _trials = Open("trials.csv");
            _events = Open("events.csv");

            WriteRow(_samples, "participant", "trial_seq", "trial_id", "attempt", "t_s", "phase",
                "head_x", "head_y", "head_z", "head_height_m",
                "rel_right_m", "rel_forward_m", "disp_from_calib_m",
                "disp_from_trial_start_m", "retreat_m", "side_m",
                "yaw_rel_deg", "yaw_change_deg", "pitch_deg", "roll_deg",
                "char_visible", "char_x", "char_z", "char_rel_right_m", "char_rel_forward_m", "head_to_char_m");

            WriteRow(_trials, "participant", "trial_seq", "trial_count", "trial_id", "attempt", "aborted", "model",
                "start_distance_m", "lateral_offset_m", "approach_angle_deg", "speed_mps", "start_delay_s",
                "stop_distance_m", "end_behaviour", "hold_time_s", "walk_past_m", "scale",
                "model_offset_x", "model_offset_y", "model_offset_z",
                "start_rel_right_m", "start_rel_forward_m",
                "max_disp_from_trial_start_m", "t_max_disp_s", "max_disp_from_calib_m",
                "max_retreat_m", "max_advance_m", "max_side_right_m", "max_side_left_m",
                "final_retreat_m", "final_side_m",
                "min_head_to_char_m", "t_min_head_to_char_s", "max_abs_yaw_change_deg",
                "duration_s", "samples");

            WriteRow(_events, "time_local", "t_session_s", "participant", "event", "details");
        }

        public void LogEvent(int participant, string evt, string details = "")
        {
            if (_events == null) return;
            WriteRow(_events,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                F(Time.realtimeSinceStartup - _sessionStartTime, "0.000"),
                participant.ToString(CultureInfo.InvariantCulture), Esc(evt), Esc(details));
            _events.Flush();
        }

        public void EndSession()
        {
            if (TrialOpen) EndTrial(aborted: true);
            _samples?.Dispose(); _samples = null;
            _trials?.Dispose(); _trials = null;
            _events?.Dispose(); _events = null;
        }

        public void Flush()
        {
            _samples?.Flush();
            _trials?.Flush();
            _events?.Flush();
        }

        // ------------------------------------------------------------------ trial

        public void BeginTrial(TrialContext ctx)
        {
            if (TrialOpen) EndTrial(aborted: true);
            _ctx = ctx;
            _calibRight = Vector3.Cross(Vector3.up, ctx.calibForward).normalized;
            _approachRight = Vector3.Cross(Vector3.up, ctx.approachDir).normalized;
            _haveStart = false;
            _sampleCount = 0;
            _lastT = 0f;
            _maxDispStart = _tMaxDispStart = _maxDispCalib = 0f;
            _maxRetreat = _maxAdvance = _maxRight = _maxLeft = 0f;
            _finalRetreat = _finalSide = 0f;
            _minCharDist = float.PositiveInfinity;
            _tMinCharDist = float.NaN;
            _maxAbsYawChange = 0f;
            TrialOpen = true;
        }

        public void Sample(float t, string phase, Vector3 headPos, Quaternion headRot, bool charVisible, Vector3 charPos)
        {
            if (!TrialOpen || _samples == null) return;

            Vector3 flat = headPos - _ctx.calibPoint;
            flat.y = 0f;
            float relRight = Vector3.Dot(flat, _calibRight);
            float relForward = Vector3.Dot(flat, _ctx.calibForward);
            float dispCalib = flat.magnitude;

            Vector3 fwd = headRot * Vector3.forward;
            Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z);
            float yawRel = fwdFlat.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(_ctx.calibForward, fwdFlat, Vector3.up) : 0f;
            Vector3 euler = headRot.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, euler.x);
            float roll = Mathf.DeltaAngle(0f, euler.z);

            if (!_haveStart)
            {
                _startFlat = flat;
                _startYaw = yawRel;
                _haveStart = true;
            }

            Vector3 d = flat - _startFlat;
            float dispStart = d.magnitude;
            float retreat = -Vector3.Dot(d, _ctx.approachDir);
            float side = Vector3.Dot(d, _approachRight);
            float yawChange = Mathf.DeltaAngle(_startYaw, yawRel);

            float charRelRight = float.NaN, charRelForward = float.NaN, headToChar = float.NaN;
            if (charVisible)
            {
                Vector3 c = charPos - _ctx.calibPoint;
                c.y = 0f;
                charRelRight = Vector3.Dot(c, _calibRight);
                charRelForward = Vector3.Dot(c, _ctx.calibForward);
                Vector3 hc = charPos - headPos;
                hc.y = 0f;
                headToChar = hc.magnitude;
                if (headToChar < _minCharDist) { _minCharDist = headToChar; _tMinCharDist = t; }
            }

            // Summary metrics
            if (dispStart > _maxDispStart) { _maxDispStart = dispStart; _tMaxDispStart = t; }
            _maxDispCalib = Mathf.Max(_maxDispCalib, dispCalib);
            _maxRetreat = Mathf.Max(_maxRetreat, retreat);
            _maxAdvance = Mathf.Max(_maxAdvance, -retreat);
            _maxRight = Mathf.Max(_maxRight, side);
            _maxLeft = Mathf.Max(_maxLeft, -side);
            _maxAbsYawChange = Mathf.Max(_maxAbsYawChange, Mathf.Abs(yawChange));
            _finalRetreat = retreat;
            _finalSide = side;
            _sampleCount++;
            _lastT = t;

            WriteRow(_samples,
                I(_ctx.participant), I(_ctx.sequenceNumber), Esc(_ctx.def.id), I(_ctx.attempt), F(t, "0.0000"), phase,
                F(headPos.x), F(headPos.y), F(headPos.z), F(headPos.y - _ctx.floorY),
                F(relRight), F(relForward), F(dispCalib),
                F(dispStart), F(retreat), F(side),
                F(yawRel, "0.00"), F(yawChange, "0.00"), F(pitch, "0.00"), F(roll, "0.00"),
                charVisible ? "1" : "0",
                charVisible ? F(charPos.x) : "", charVisible ? F(charPos.z) : "",
                F(charRelRight), F(charRelForward), F(headToChar));
        }

        public void EndTrial(bool aborted)
        {
            if (!TrialOpen) return;
            TrialOpen = false;
            if (_trials == null) return;

            var def = _ctx.def;
            float startRight = _haveStart ? Vector3.Dot(_startFlat, _calibRight) : float.NaN;
            float startForward = _haveStart ? Vector3.Dot(_startFlat, _ctx.calibForward) : float.NaN;
            float minDist = float.IsPositiveInfinity(_minCharDist) ? float.NaN : _minCharDist;

            WriteRow(_trials,
                I(_ctx.participant), I(_ctx.sequenceNumber), I(_ctx.sequenceLength), Esc(def.id), I(_ctx.attempt),
                aborted ? "1" : "0", Esc(_ctx.modelName),
                F(def.startDistance), F(def.lateralOffset), F(def.approachAngle), F(def.speed), F(def.startDelay),
                F(def.stopDistance), def.endBehaviour.ToString(), F(def.holdTime), F(def.walkPastDistance), F(def.scale),
                F(def.modelPositionOffset.x), F(def.modelPositionOffset.y), F(def.modelPositionOffset.z),
                F(startRight), F(startForward),
                F(_maxDispStart), F(_tMaxDispStart, "0.000"), F(_maxDispCalib),
                F(_maxRetreat), F(_maxAdvance), F(_maxRight), F(_maxLeft),
                F(_finalRetreat), F(_finalSide),
                F(minDist), F(_tMinCharDist, "0.000"), F(_maxAbsYawChange, "0.00"),
                F(_lastT, "0.000"), I(_sampleCount));
            Flush();
        }

        // ------------------------------------------------------------------ helpers

        StreamWriter Open(string fileName)
        {
            // UTF-8 with BOM so Excel detects the encoding.
            return new StreamWriter(Path.Combine(SessionFolder, fileName), false, new UTF8Encoding(true));
        }

        void WriteRow(StreamWriter w, params string[] cells)
        {
            if (w == null) return;
            w.WriteLine(string.Join(_sep, cells));
        }

        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        string F(float v, string format = "0.0000")
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "";
            string s = v.ToString(format, CultureInfo.InvariantCulture);
            return _decimalComma ? s.Replace('.', ',') : s;
        }

        string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(_sep) || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
