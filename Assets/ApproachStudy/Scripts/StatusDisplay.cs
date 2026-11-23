using TMPro;
using UnityEngine;

namespace ApproachStudy
{
    /// <summary>
    /// A small text panel that floats in front of the headset and lazily follows the head.
    /// It is meant for the person running the session (cast the headset view to a phone/PC to read it).
    /// </summary>
    public class StatusDisplay : MonoBehaviour
    {
        public float distance = 1.3f;
        public float heightOffset = -0.2f;
        public float followSpeed = 3f;

        Transform _head;
        TextMeshPro _text;
        GameObject _visualRoot;

        public static StatusDisplay Create(Transform head, Material backgroundMaterial = null)
        {
            var go = new GameObject("StatusDisplay");
            var display = go.AddComponent<StatusDisplay>();
            display.Build(head, backgroundMaterial);
            return display;
        }

        void Build(Transform head, Material backgroundMaterial)
        {
            _head = head;
            _visualRoot = new GameObject("Visual");
            _visualRoot.transform.SetParent(transform, false);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_visualRoot.transform, false);
            _text = textGo.AddComponent<TextMeshPro>();
            _text.rectTransform.sizeDelta = new Vector2(0.9f, 0.5f);
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 0.05f;
            _text.fontSizeMax = 0.4f;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.color = Color.white;
            _text.richText = true;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(_visualRoot.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            bg.transform.localScale = new Vector3(0.96f, 0.56f, 1f);
            ExperimentController.ApplyMaterial(bg.GetComponent<Renderer>(), backgroundMaterial, new Color(0.05f, 0.05f, 0.07f));

            SnapToHead();
        }

        public void SetText(string text)
        {
            if (_text != null && _text.text != text) _text.text = text;
        }

        public void SetVisible(bool visible)
        {
            if (_visualRoot != null && _visualRoot.activeSelf != visible)
            {
                _visualRoot.SetActive(visible);
                if (visible) SnapToHead();
            }
        }

        Vector3 TargetPosition()
        {
            Vector3 f = _head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = _head.up;
            f.y = 0f;
            f.Normalize();
            return _head.position + f * distance + Vector3.up * heightOffset;
        }

        void SnapToHead()
        {
            if (_head == null) return;
            transform.position = TargetPosition();
            FaceHead();
        }

        void FaceHead()
        {
            Vector3 away = transform.position - _head.position;
            if (away.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        void LateUpdate()
        {
            if (_head == null) return;
            transform.position = Vector3.Lerp(transform.position, TargetPosition(), 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            FaceHead();
        }
    }
}
