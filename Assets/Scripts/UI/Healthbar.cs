using UnityEngine;
using UnityEngine.UI;

namespace Ilumisoft.HealthSystem.UI
{
    [AddComponentMenu("Health System/UI/Healthbar")]
    public class Healthbar : MonoBehaviour
    {
        [field:SerializeField]
        public HealthComponent Health { get; set; }

        [SerializeField]
        Canvas canvas;

        [SerializeField]
        Image fillImage;

        [SerializeField, Tooltip("Whether the healthbar should be hidden when health is empty")]
        bool hideEmpty = false;

        [SerializeField, Tooltip("Makes the healthbar being aligned with the camera")]
        bool alignWithCamera = false;

        [SerializeField, Min(0.1f), Tooltip("Controls how fast changes will be animated in points/second")]
        float changeSpeed = 100;

        float currentValue;
        bool started;   // has currentValue been set from a real Health yet?

        protected virtual void Reset()
        {
            if (Health == null)
            {
                Health = GetComponentInParent<HealthComponent>();
            }
        }

        private void Start()
        {
            // A bar that is given its Health later (the co-op health bars: CoopHealthbarBinder hands it the player once
            // that player exists) has none yet. It just waits, instead of throwing an error every frame.
            if (Health != null)
            {
                currentValue = Health.CurrentHealth;
                started = true;
            }
        }

        private void Update()
        {
            if (alignWithCamera)
            {
                AlignWithCamera();
            }

            if (Health == null || fillImage == null) return;

            if (!started)
            {
                currentValue = Health.CurrentHealth;   // the Health arrived after Start: begin from where it really is
                started = true;
            }

            currentValue = Mathf.MoveTowards(currentValue, Health.CurrentHealth, Time.deltaTime * changeSpeed);
            
            UpdateFillbar();
            UpdateVisibility();
        }

        private void AlignWithCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) transform.forward = cam.transform.forward;
        }

        void UpdateFillbar()
        {
            // Update the fill amount
            float value = Mathf.InverseLerp(0, Health.MaxHealth, currentValue);

            fillImage.fillAmount = value;
        }

        void UpdateVisibility()
        {
            float value = fillImage.fillAmount;

            if (canvas != null)
            {
                // Hide if empty
                if (Mathf.Approximately(value, 0))
                {
                    if (hideEmpty && canvas.gameObject.activeSelf)
                    {
                        canvas.gameObject.SetActive(false);
                    }
                }
                // Make sure the canvas is enabled if health is not empty
                else if (value > 0 && canvas.gameObject.activeSelf == false)
                {
                    canvas.gameObject.SetActive(true);
                }
            }
        }
    }
}