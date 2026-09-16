using UnityEngine;

namespace TacSim
{
    // Detail selection changes render geometry only; one shared vehicle/collider drives both.
    public sealed class RovVisuals : MonoBehaviour
    {
        public GameObject lowModel;
        public GameObject highModel;
        public bool HighDetail { get; private set; }
        public string DetailName => HighDetail ? "HIGH POLY" : "LOW POLY";
        GameObject lowInstance, highInstance;
        public Bounds ActiveBounds
        {
            get
            {
                var instance = HighDetail ? highInstance : lowInstance;
                var renderers = instance.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                return bounds;
            }
        }

        void Awake()
        {
            if (lowModel == null || highModel == null)
            {
                Debug.LogError("Both Malstrom models are required. Sync Drive assets and rebuild.");
                return;
            }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            SetHighDetail(false);
        }

        public void Toggle() => SetHighDetail(!HighDetail);

        public void SetHighDetail(bool high)
        {
            if (lowModel == null || highModel == null) return;
            if (high && highInstance == null) highInstance = CreateModel(highModel);
            if (!high && lowInstance == null) lowInstance = CreateModel(lowModel);
            if (lowInstance != null) lowInstance.SetActive(!high);
            if (highInstance != null) highInstance.SetActive(high);
            HighDetail = high;
        }

        GameObject CreateModel(GameObject prefab)
        {
            var model = Instantiate(prefab, transform, false);
            model.name = prefab.name;
            // Export puts the domed enclosure toward -Z; use that end as vehicle forward.
            model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
            // CAD origin is not the hull center. Match both variants using their common bounds.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                model.transform.position += transform.position - bounds.center;
            }
            return model;
        }
    }
}
