using System;
using UnityEngine;

namespace ApproachStudy
{
    /// <summary>What the character does when it reaches the end of its path.</summary>
    public enum EndBehaviour
    {
        /// <summary>Stops at Stop Distance, waits Hold Time, then disappears.</summary>
        StopThenVanish,
        /// <summary>Stops at Stop Distance and stays visible until the next trial starts.</summary>
        StopAndStay,
        /// <summary>Ignores Stop Distance, walks past the participant by Walk Past Distance, then disappears.</summary>
        WalkPastThenVanish
    }

    /// <summary>
    /// One kind of approach. All distances are in metres, angles in degrees, speed in metres/second.
    /// Positions are relative to the calibrated spot and the direction the participant faced when calibrated.
    /// </summary>
    [Serializable]
    public class TrialDefinition
    {
        [Tooltip("Short unique name used in orders and in the data files, e.g. 'near_fast'. No commas.")]
        public string id = "trial";

        [Header("Character model")]
        [Tooltip("Leave empty to use a simple cube. Drag a prefab here to use another model.")]
        public GameObject characterPrefab;

        [Tooltip("Cube colour (only used when no prefab is set).")]
        public Color cubeColor = new Color(0.95f, 0.45f, 0.2f);

        [Tooltip("Cube size in metres: width, height, depth (only used when no prefab is set).")]
        public Vector3 cubeSize = new Vector3(0.45f, 1.75f, 0.3f);

        [Tooltip("Uniform size multiplier for the cube or prefab. 1 = normal, 1.2 = 20% bigger.")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("Shifts the model away from its walking path, in the character's own space " +
                 "(x = character's right, y = up, z = character's forward). Use this when a model's pivot is off-centre " +
                 "or to deliberately offset the visible body from the path.")]
        public Vector3 modelPositionOffset = Vector3.zero;

        [Tooltip("Extra rotation of the model, in degrees. E.g. (0,180,0) if a prefab faces backwards.")]
        public Vector3 modelRotationOffset = Vector3.zero;

        [Header("Path (relative to the calibrated spot)")]
        [Tooltip("How far from the calibrated spot the character starts (metres).")]
        [Min(0f)] public float startDistance = 4f;

        [Tooltip("Sideways offset of the path (metres). 0 = walks straight at the participant. " +
                 "+ = passes on the participant's right, - = on their left (as seen by the participant facing the character).")]
        public float lateralOffset = 0f;

        [Tooltip("Direction the character comes from (degrees). 0 = straight in front of where the participant faced " +
                 "at calibration, +30 = from 30 degrees to their right, -30 = from their left.")]
        [Range(-180f, 180f)] public float approachAngle = 0f;

        [Header("Motion")]
        [Tooltip("Walking speed in metres per second. Normal human walking is about 1.2-1.4 m/s.")]
        [Min(0.01f)] public float speed = 1.2f;

        [Tooltip("Seconds the character stands still at its start point before it starts walking.")]
        [Min(0f)] public float startDelay = 1f;

        [Tooltip("Where the character stops, measured along its path from the end point next to the participant (metres). " +
                 "With Lateral Offset 0 this is simply the distance from the calibrated spot.")]
        [Min(0f)] public float stopDistance = 1f;

        public EndBehaviour endBehaviour = EndBehaviour.StopThenVanish;

        [Tooltip("Seconds the character stays after stopping (Stop Then Vanish / Stop And Stay).")]
        [Min(0f)] public float holdTime = 2f;

        [Tooltip("For Walk Past Then Vanish: how far past the participant it keeps walking (metres).")]
        [Min(0f)] public float walkPastDistance = 2f;

        public string ModelName => characterPrefab != null ? characterPrefab.name : "Cube";

        public TrialDefinition Clone() => (TrialDefinition)MemberwiseClone();
    }
}
