using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacSim
{
    // Appearance only: never changes vehicle forces, controls or mission state.
    public sealed class WaterAppearance : MonoBehaviour
    {
        public Camera targetCamera;
        public Light overheadLight;
        public ParticleSystem suspendedParticles;
        public Renderer[] causticSurfaces;
        public Renderer waterSurface;
        public Light[] poolLights;
        public Volume effects;
        public static readonly string[] PresetNames = { "Clear pool", "Coastal water", "Turbid water", "Night dive" };
        public int Preset { get; private set; }
        public bool FiltersEnabled { get; private set; } = true;
        public bool ParticlesEnabled { get; private set; } = true;
        public bool CausticsEnabled { get; private set; } = true;
        public float Visibility { get; private set; } = 24;
        public bool MenuOpen { get; set; }
        public string Name => PresetNames[Preset];
        Color waterColor;
        VolumeProfile runtimeProfile;
        ColorAdjustments grade;
        Vignette vignette;
        FilmGrain grain;
        Bloom bloom;
        bool wasUnderwater = true;
        MaterialPropertyBlock surfaceProperties;

        void Awake()
        {
            surfaceProperties = new MaterialPropertyBlock();
            // Volume owns a private runtime copy; the authored profile stays unchanged.
            runtimeProfile = effects.profile;
            if (!runtimeProfile.TryGet(out grade)) grade = runtimeProfile.Add<ColorAdjustments>(true);
            if (!runtimeProfile.TryGet(out vignette)) vignette = runtimeProfile.Add<Vignette>(true);
            if (!runtimeProfile.TryGet(out grain)) grain = runtimeProfile.Add<FilmGrain>(true);
            if (!runtimeProfile.TryGet(out bloom)) bloom = runtimeProfile.Add<Bloom>(true);
            bloom.threshold.value = 1.1f;
            bloom.intensity.value = 0.22f;
            vignette.intensity.value = 0.2f;
            vignette.smoothness.value = 0.55f;
            ApplyPreset(0);
        }

        public void CyclePreset() => ApplyPreset((Preset + 1) % PresetNames.Length);
        public void ApplyPreset(int index)
        {
            Preset = Mathf.Clamp(index, 0, PresetNames.Length - 1);
            float[] visibility = { 24, 13, 6, 10 };
            Color[] colors = { new(0.06f, 0.25f, 0.29f), new(0.055f, 0.19f, 0.16f),
                new(0.15f, 0.19f, 0.11f), new(0.006f, 0.018f, 0.03f) };
            waterColor = colors[Preset];
            Visibility = visibility[Preset];
            overheadLight.intensity = Preset == 3 ? 0.06f : (Preset == 2 ? 0.65f : 1.5f);
            RenderSettings.ambientLight = Preset == 3 ? new Color(0.025f, 0.04f, 0.055f) : new Color(0.22f, 0.32f, 0.35f);
            foreach (Light lamp in poolLights) lamp.intensity = Preset == 3 ? 0.15f : 1.5f;
            surfaceProperties.SetFloat("_LightLevel", Preset == 3 ? 0.06f : Preset == 2 ? 0.6f : 1);
            waterSurface.SetPropertyBlock(surfaceProperties);
            grade.colorFilter.value = Preset == 0 ? new Color(0.9f, 0.98f, 1) :
                Preset == 3 ? new Color(0.65f, 0.83f, 1) : new Color(0.82f, 0.96f, 0.78f);
            grade.contrast.value = Preset == 0 ? 10 : -6;
            grade.saturation.value = Preset == 0 ? -5 : -20;
            grade.postExposure.value = Preset == 3 ? -0.4f : 0;
            grain.intensity.value = Preset == 0 ? 0.07f : 0.22f;
            ApplyAppearance();
        }

        public void SetVisibility(float metres)
        {
            Visibility = Mathf.Clamp(metres, 3, 35);
            ApplyAppearance();
        }
        public void SetFilters(bool enabled) { FiltersEnabled = enabled; ApplyAppearance(); }
        public void SetParticles(bool enabled) { ParticlesEnabled = enabled; ApplyAppearance(); }
        public void SetCaustics(bool enabled) { CausticsEnabled = enabled; ApplyAppearance(); }

        void LateUpdate()
        {
            bool underwater = targetCamera.transform.position.y < 0;
            if (underwater != wasUnderwater) ApplyAppearance();
        }

        void ApplyAppearance()
        {
            wasUnderwater = targetCamera.transform.position.y < 0;
            RenderSettings.fog = wasUnderwater;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Visibility is an artistic fog range, not calibrated optical attenuation.
            RenderSettings.fogDensity = 1.35f / Visibility;
            RenderSettings.fogColor = waterColor;
            targetCamera.backgroundColor = wasUnderwater ? waterColor : new Color(0.14f, 0.2f, 0.24f);
            effects.weight = FiltersEnabled && wasUnderwater ? 1 : 0;
            foreach (Renderer surface in causticSurfaces)
                surface.enabled = CausticsEnabled && Preset != 3;
            var emission = suspendedParticles.emission;
            emission.enabled = ParticlesEnabled;
            emission.rateOverTime = Preset == 2 ? 45 : 15;
            if (!ParticlesEnabled) suspendedParticles.Clear();
        }

    }
}
