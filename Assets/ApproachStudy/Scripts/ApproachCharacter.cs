using UnityEngine;

namespace ApproachStudy
{
    /// <summary>
    /// The walking character. Created by the ExperimentController for each trial and moved by it every frame
    /// (via Tick) so motion and recording stay in sync.
    /// If the model has an Animator with a float parameter called "Speed", it is set to the walking speed while
    /// walking and to 0 while standing - handy for animated characters later.
    /// </summary>
    public class ApproachCharacter : MonoBehaviour
    {
        public enum Phase { Waiting, Walking, Holding, Finished }

        public Phase CurrentPhase { get; private set; }
        public bool IsVisible { get; private set; } = true;
        public string ModelName { get; private set; }
        public Vector3 PathStart { get; private set; }
        public Vector3 PathEnd { get; private set; }

        const string AnimatorSpeedParameter = "Speed";

        TrialDefinition _def;
        float _timer;
        Animator _animator;
        bool _animatorHasSpeed;

        /// <summary>Creates the character standing at <paramref name="start"/>, ready to walk to <paramref name="end"/>.</summary>
        public static ApproachCharacter Spawn(TrialDefinition def, Vector3 start, Vector3 end, Material cubeMaterial = null)
        {
            var root = new GameObject($"Character_{def.id}");
            Vector3 walkDir = end - start;
            walkDir.y = 0f;
            if (walkDir.sqrMagnitude < 1e-6f) walkDir = Vector3.forward;
            root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(walkDir.normalized, Vector3.up));

            GameObject model;
            if (def.characterPrefab != null)
            {
                model = Instantiate(def.characterPrefab, root.transform, false);
                model.transform.localScale = def.characterPrefab.transform.localScale * def.scale;
                model.transform.localPosition = def.modelPositionOffset;
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.name = "Cube";
                Destroy(model.GetComponent<Collider>());
                model.transform.SetParent(root.transform, false);
                Vector3 size = def.cubeSize * def.scale;
                model.transform.localScale = size;
                model.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f) + def.modelPositionOffset;
                ExperimentController.ApplyMaterial(model.GetComponent<Renderer>(), cubeMaterial, def.cubeColor);

                // A dark "visor" at head height on the front, so you can see which way the cube faces.
                var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visor.name = "Visor";
                Destroy(visor.GetComponent<Collider>());
                visor.transform.SetParent(model.transform, false);
                visor.transform.localPosition = new Vector3(0f, 0.36f, 0.5f + 0.02f / Mathf.Max(size.z, 0.01f));
                visor.transform.localScale = new Vector3(0.7f, 0.07f / Mathf.Max(size.y, 0.01f), 0.04f / Mathf.Max(size.z, 0.01f));
                ExperimentController.ApplyMaterial(visor.GetComponent<Renderer>(), cubeMaterial, new Color(0.08f, 0.08f, 0.1f));
            }
            model.transform.localRotation = Quaternion.Euler(def.modelRotationOffset);

            var character = root.AddComponent<ApproachCharacter>();
            character.Init(def, start, end);
            return character;
        }

        void Init(TrialDefinition def, Vector3 start, Vector3 end)
        {
            _def = def;
            ModelName = def.ModelName;
            PathStart = start;
            PathEnd = end;
            transform.position = start;
            CurrentPhase = Phase.Waiting;
            _timer = def.startDelay;

            _animator = GetComponentInChildren<Animator>();
            if (_animator != null && _animator.runtimeAnimatorController != null)
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Float && p.name == AnimatorSpeedParameter)
                        _animatorHasSpeed = true;
            SetAnimatorSpeed(0f);
        }

        /// <summary>Advances the character by <paramref name="dt"/> seconds.</summary>
        public void Tick(float dt)
        {
            switch (CurrentPhase)
            {
                case Phase.Waiting:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        CurrentPhase = Phase.Walking;
                        SetAnimatorSpeed(_def.speed);
                    }
                    break;

                case Phase.Walking:
                    transform.position = Vector3.MoveTowards(transform.position, PathEnd, Mathf.Max(0.01f, _def.speed) * dt);
                    if ((transform.position - PathEnd).sqrMagnitude < 1e-8f)
                    {
                        SetAnimatorSpeed(0f);
                        if (_def.endBehaviour == EndBehaviour.WalkPastThenVanish)
                        {
                            Finish(hide: true);
                        }
                        else
                        {
                            CurrentPhase = Phase.Holding;
                            _timer = _def.holdTime;
                        }
                    }
                    break;

                case Phase.Holding:
                    _timer -= dt;
                    if (_timer <= 0f) Finish(hide: _def.endBehaviour == EndBehaviour.StopThenVanish);
                    break;
            }
        }

        void Finish(bool hide)
        {
            CurrentPhase = Phase.Finished;
            if (hide) SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        void SetAnimatorSpeed(float value)
        {
            if (_animatorHasSpeed) _animator.SetFloat(AnimatorSpeedParameter, value);
        }
    }
}
