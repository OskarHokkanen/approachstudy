using System;
using System.Collections.Generic;
using UnityEngine;

namespace ApproachStudy
{
    public enum OrderMode
    {
        /// <summary>Trials in the order they are listed, repeated Repetitions times.</summary>
        AsListed,
        /// <summary>One of the Custom Orders you typed in (e.g. one per participant).</summary>
        Custom,
        /// <summary>Random order. Each repetition block is shuffled separately. The seed is saved with the data.</summary>
        Shuffled,
        /// <summary>Balanced Latin square (Williams design): the row is chosen from the participant number,
        /// so order and carry-over effects are counterbalanced across participants.</summary>
        LatinSquare
    }

    [Serializable]
    public class TrialOrder
    {
        [Tooltip("Name shown in the headset, e.g. 'Participant 1' or 'Order A'.")]
        public string name = "Order A";

        [Tooltip("Trial ids separated by commas, e.g. 'baseline, fast, offset_right'. " +
                 "You can also use trial numbers (1 = first trial in the list). Repeats are allowed.")]
        [TextArea(1, 4)] public string trialIds = "";
    }

    /// <summary>
    /// All settings for one study. Create more via Assets > Create > Approach Study > Experiment Config
    /// and drag the one you want into the Experiment Controller.
    /// </summary>
    [CreateAssetMenu(menuName = "Approach Study/Experiment Config", fileName = "ExperimentConfig")]
    public class ExperimentConfig : ScriptableObject
    {
        [Tooltip("The kinds of approach in this study. Each needs a unique id.")]
        public List<TrialDefinition> trials = new List<TrialDefinition>();

        [Header("Ordering")]
        [Tooltip("The order selected when the app starts. You can change it in the headset with the right thumbstick (left/right).")]
        public OrderMode defaultOrderMode = OrderMode.AsListed;

        [Tooltip("Which custom order is selected first when Default Order Mode is Custom (0 = first).")]
        [Min(0)] public int defaultCustomOrderIndex = 0;

        [Tooltip("Hand-written orders. Use as many as you like, e.g. one per participant.")]
        public List<TrialOrder> customOrders = new List<TrialOrder>();

        [Tooltip("How many times the whole trial list is run (As Listed / Shuffled / Latin Square). Custom orders are used exactly as written.")]
        [Min(1)] public int repetitions = 1;

        [Tooltip("0 = a new random seed every session (it is saved in session.txt). Any other number gives the same shuffle every time.")]
        public int shuffleSeed = 0;

        [Header("Session flow")]
        [Tooltip("If on, the next character is sent automatically after Inter Trial Interval. If off, press A for each trial.")]
        public bool autoAdvance = false;

        [Tooltip("Seconds between trials when Auto Advance is on.")]
        [Min(0f)] public float interTrialInterval = 3f;

        [Tooltip("Seconds to keep recording after the character has finished (captures late reactions).")]
        [Min(0f)] public float postTrialRecordTime = 1.5f;

        [Header("Recording")]
        [Tooltip("Samples per second. 0 = every rendered frame (72-120 Hz on Quest, depending on refresh rate).")]
        [Min(0f)] public float sampleRate = 0f;

        [Tooltip("Write CSV with ';' between columns and ',' as decimal mark, so Swedish/European Excel opens it directly. " +
                 "Turn off for R / Python / English Excel (',' and '.').")]
        public bool europeanExcelFormat = false;

        // ---------------------------------------------------------------------------------------------

        public int FindTrialIndex(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return -1;
            id = id.Trim();
            for (int i = 0; i < trials.Count; i++)
                if (string.Equals(trials[i].id?.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return i;
            // Allow 1-based trial numbers too.
            if (int.TryParse(id, out int number) && number >= 1 && number <= trials.Count)
                return number - 1;
            return -1;
        }

        /// <summary>Returns the list of trial indices to run, in order.</summary>
        public List<int> BuildOrder(OrderMode mode, int customOrderIndex, int participantNumber, int seed, List<string> warnings = null)
        {
            var result = new List<int>();
            int n = trials.Count;
            if (n == 0) return result;
            int reps = Mathf.Max(1, repetitions);

            switch (mode)
            {
                case OrderMode.AsListed:
                    for (int r = 0; r < reps; r++)
                        for (int i = 0; i < n; i++) result.Add(i);
                    break;

                case OrderMode.Shuffled:
                {
                    var rng = new System.Random(seed);
                    for (int r = 0; r < reps; r++)
                    {
                        var block = new List<int>();
                        for (int i = 0; i < n; i++) block.Add(i);
                        for (int i = n - 1; i > 0; i--)
                        {
                            int j = rng.Next(i + 1);
                            (block[i], block[j]) = (block[j], block[i]);
                        }
                        result.AddRange(block);
                    }
                    break;
                }

                case OrderMode.LatinSquare:
                {
                    var row = BalancedLatinSquareRow(n, Mathf.Max(0, participantNumber - 1));
                    for (int r = 0; r < reps; r++) result.AddRange(row);
                    break;
                }

                case OrderMode.Custom:
                {
                    if (customOrderIndex < 0 || customOrderIndex >= customOrders.Count)
                    {
                        warnings?.Add("Custom order not found - using As Listed.");
                        return BuildOrder(OrderMode.AsListed, 0, participantNumber, seed, warnings);
                    }
                    var tokens = customOrders[customOrderIndex].trialIds.Split(
                        new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var raw in tokens)
                    {
                        var token = raw.Trim();
                        if (token.Length == 0) continue;
                        int idx = FindTrialIndex(token);
                        if (idx < 0) warnings?.Add($"Unknown trial '{token}' in '{customOrders[customOrderIndex].name}' - skipped.");
                        else result.Add(idx);
                    }
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Row of a balanced Latin square (Williams design). Each condition appears once per row,
        /// and across n (even) or 2n (odd) participants every condition follows every other equally often.
        /// </summary>
        public static List<int> BalancedLatinSquareRow(int n, int row)
        {
            var result = new List<int>(n);
            int j = 0, h = 0;
            for (int i = 0; i < n; i++)
            {
                int val;
                if (i < 2 || i % 2 != 0) val = j++;
                else val = n - h++ - 1;
                result.Add((val + row) % n);
            }
            if (n % 2 != 0 && row % 2 != 0) result.Reverse();
            return result;
        }

        void OnValidate()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in trials)
            {
                if (t == null) continue;
                if (string.IsNullOrWhiteSpace(t.id)) Debug.LogWarning($"[ApproachStudy] {name}: a trial has no id.", this);
                else if (!seen.Add(t.id.Trim())) Debug.LogWarning($"[ApproachStudy] {name}: duplicate trial id '{t.id}'.", this);
                else if (t.id.Contains(",") || t.id.Contains(";")) Debug.LogWarning($"[ApproachStudy] {name}: trial id '{t.id}' should not contain ',' or ';'.", this);
            }
        }

        /// <summary>Fills a new config with example trials.</summary>
        public void FillWithExamples()
        {
            trials = new List<TrialDefinition>
            {
                new TrialDefinition { id = "baseline" },
                new TrialDefinition { id = "fast", speed = 2.0f },
                new TrialDefinition { id = "slow", speed = 0.7f },
                new TrialDefinition { id = "offset_right", lateralOffset = 0.5f, cubeColor = new Color(0.3f, 0.6f, 1f) },
                new TrialDefinition { id = "big", scale = 1.25f, cubeColor = new Color(0.8f, 0.2f, 0.3f) },
                new TrialDefinition { id = "walk_past", lateralOffset = 0.6f, endBehaviour = EndBehaviour.WalkPastThenVanish, cubeColor = new Color(0.4f, 0.8f, 0.4f) },
            };
            customOrders = new List<TrialOrder>
            {
                new TrialOrder { name = "Order A", trialIds = "baseline, fast, slow, offset_right, big, walk_past" },
                new TrialOrder { name = "Order B", trialIds = "walk_past, big, offset_right, slow, fast, baseline" },
            };
        }
    }
}
