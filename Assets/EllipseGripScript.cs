using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public class EllipseGripScript : MonoBehaviour
{
    private List<ResizableWheelData> ResizableWheelDataList;

    private float TryUpdateListsTimer = 0f;
    private float TryUpdateListsPeriod = 10f;

    private bool HasCompletedReflection = false;
    private Type RwcType;
    private TypeInfo RwcTypeInfo;
    private PropertyInfo ForwardSlipInfo;
    private PropertyInfo SidewaysSlipInfo;
    private PropertyInfo ForwardFrictionInfo;
    private PropertyInfo SidewaysFrictionInfo;
    private FieldInfo SurfaceNormalInfo;
    private FieldInfo WheelRadiusInfo;

    private TypeInfo FrictionCurveTypeInfo;
    private PropertyInfo FrictionCurveAsymptoteSlipInfo;
    private PropertyInfo FrictionCurveAsymptoteValueInfo;
    private PropertyInfo FrictionCurveExtremumSlipInfo;
    private PropertyInfo FrictionCurveExtremumValueInfo;
    private PropertyInfo FrictionCurveStiffnessInfo;

    private bool ParticlesEnabled = true;
    private float ParticleTimer = 0f;
    private float ParticlePeriod = 0.02f;
    private ParticleSystem.EmitParams OverrideParams = new ParticleSystem.EmitParams();

    private Color ParticleColor = Color.white;
    private float ParticleAlphaPower = 3f;

    [SerializeField] private GameObject SkidmarkObjectPrefab;
    private bool SkidmarksEnabled = true;
    private float SkidmarksTimer = 0f;
    private float SkidmarksPeriod = 0.02f;
    private float SkidmarksMaximumOpacity = 1f;
    private float SkidmarksMaximumOverload = 0.33f;

    // Maximum loss of grip perpendicular to current load
    private float EffectStrength = 0.25f;

    // Save file
    string PathNachsave;
    string PathSettings;
    string NameNachsave = "NACHSAVE";
    string NameSettings = "EGS.DAT";
    string FileHeaderV0 = "EGSAVE0";
    /*
     * Saved settings definition v.0 (BinaryReader/Writer)
     * string       FileHeaderV0
     * bool         DebugMode
     * bool         ParticlesEnabled
     * float        ParticleColor.r
     * float        ParticleColor.g
     * float        ParticleColor.b
     * float        ParticleColor.a
     * float        ParticleAlphaPower
     * bool         SkidmarksEnabled
     * float        EffectStrength
     */

    private bool DebugMode = false;
    private StringBuilder DebugText;

    private void Start()
    {
        ResizableWheelDataList = new List<ResizableWheelData>(10);
        DebugText = new StringBuilder(1000);

        ServiceProvider.Instance.DevConsole.RegisterCommand("EllipseGrip_ToggleDebugMode", ToggleDebugMode);
        ServiceProvider.Instance.DevConsole.RegisterCommand("EllipseGrip_ToggleParticles", ToggleParticles);
        ServiceProvider.Instance.DevConsole.RegisterCommand("EllipseGrip_ToggleSkidmarks", ToggleSkidmarks);
        ServiceProvider.Instance.DevConsole.RegisterCommand<Color>("EllipseGrip_SetParticleColor", SetParticleColor);
        ServiceProvider.Instance.DevConsole.RegisterCommand<float>("EllipseGrip_SetParticleAlphaExponent", SetParticleAlphaExponent);
        ServiceProvider.Instance.DevConsole.RegisterCommand<float>("EllipseGrip_SetStrength", SetEffectStrength);

        PathNachsave = Path.Combine(Application.persistentDataPath, NameNachsave);
        PathSettings = Path.Combine(PathNachsave, NameSettings);

        if (!Directory.Exists(PathNachsave))
        {
            Directory.CreateDirectory(PathNachsave);
        }

        if (File.Exists(PathSettings))
        {
            // Load settings
            BinaryReader reader = new BinaryReader(File.OpenRead(PathSettings));
            try
            {
                string header = reader.ReadString();
                if (header == FileHeaderV0)
                {
                    bool _debugMode = reader.ReadBoolean();
                    bool _particlesEnabled = reader.ReadBoolean();
                    float _particleColor_r = reader.ReadSingle();
                    float _particleColor_g = reader.ReadSingle();
                    float _particleColor_b = reader.ReadSingle();
                    float _particleColor_a = reader.ReadSingle();
                    float _particleAlphaPower = reader.ReadSingle();
                    bool _skidmarksEnabled = reader.ReadBoolean();
                    float _effectStrength = reader.ReadSingle();

                    DebugMode = _debugMode;
                    ParticlesEnabled = _particlesEnabled;
                    ParticleColor = new Color(_particleColor_r, _particleColor_g, _particleColor_b, _particleColor_a);
                    ParticleAlphaPower = _particleAlphaPower;
                    SkidmarksEnabled = _skidmarksEnabled;
                    EffectStrength = _effectStrength;
                }
                else
                {
                    Debug.LogError($"Failed to read ellipse-grip settings data: invalid header \"{header}\"");
                }

                Debug.Log("Successfully read ellipse-grip settings data");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to read ellipse-grip settings data: {ex.Message}");
            }
            reader.Close();
        }
    }

    private void FixedUpdate()
    {
        transform.position = -ServiceProvider.Instance.GameWorld.FloatingOriginOffset;

        TryUpdateListsTimer -= Time.fixedDeltaTime;
        if (TryUpdateListsTimer <= 0f)
        {
            TryUpdateListsTimer = TryUpdateListsPeriod;

            if (!HasCompletedReflection)
            {
                // Component returnedComponent = null;

                Component[] allComponents = FindObjectsOfType<Component>();
                foreach (Component c in allComponents)
                {
                    if (c.GetType().Name == "ResizableWheelCollider")
                    {
                        // returnedComponent = c;
                        RwcType = c.GetType();
                        RwcTypeInfo = c.GetType().GetTypeInfo();
                        break;
                    }
                }
                if (RwcType == null)
                {
                    // Failed to get the type
                    return;
                }

                ForwardSlipInfo = RwcTypeInfo.GetProperty("ForwardSlip");
                SidewaysSlipInfo = RwcTypeInfo.GetProperty("SidewaysSlip");
                ForwardFrictionInfo = RwcTypeInfo.GetProperty("ForwardFriction");
                SidewaysFrictionInfo = RwcTypeInfo.GetProperty("SidewaysFriction");
                SurfaceNormalInfo = RwcTypeInfo.GetField("_surfaceNormal", BindingFlags.NonPublic | BindingFlags.Instance);
                WheelRadiusInfo = RwcTypeInfo.GetField("WheelRadius");

                FrictionCurveTypeInfo = ForwardFrictionInfo.PropertyType.GetTypeInfo();
                FrictionCurveAsymptoteSlipInfo = FrictionCurveTypeInfo.GetProperty("AsymptoteSlip");
                FrictionCurveAsymptoteValueInfo = FrictionCurveTypeInfo.GetProperty("AsymptoteValue");
                FrictionCurveExtremumSlipInfo = FrictionCurveTypeInfo.GetProperty("ExtremumSlip");
                FrictionCurveExtremumValueInfo = FrictionCurveTypeInfo.GetProperty("ExtremumValue");
                FrictionCurveStiffnessInfo = FrictionCurveTypeInfo.GetProperty("Stiffness");

                /*
                 * Properties in WheelFrictionCurveSource:
                 * float AsymptoteSlip = ResizableWheel.slipForwardAsymptote
                 * float AsymptoteValue = 1.125 * ResizableWheel.traction[Forward|Sideways]
                 * float ExtremumSlip = ResizableWheel.slipSidewaysExtremum
                 * float ExtremumValue = 1.5 * ResizableWheel.traction[Forward|Sideways]
                 * float Stiffness = 1 (always)
                 * Note capitalized first characters.
                 * 
                PropertyInfo[] wfcsProperties = FrictionCurveTypeInfo.GetProperties();
                foreach (PropertyInfo p in wfcsProperties)
                {
                    Type pType = p.PropertyType;
                    string pName = p.Name;
                    object pValue = p.GetValue(SidewaysFrictionInfo.GetValue(returnedComponent));
                    Debug.Log($"WFCS Property: {pType} {pName} = {pValue}");
                }
                */

                HasCompletedReflection = true;
            }

            if (HasCompletedReflection)
            {
                // Search for ResizableWheelCollider components and add them if they are not in the list
                Component[] allComponents = FindObjectsOfType<Component>();
                foreach (Component c in allComponents)
                {
                    bool isInList = false;
                    foreach (ResizableWheelData d in ResizableWheelDataList)
                    {
                        if (c == d.collider)
                        {
                            isInList = true;
                        }
                    }

                    if (c.GetType().Name == "ResizableWheelCollider" && !isInList)
                    {
                        object forwardFriction = ForwardFrictionInfo.GetValue(c);
                        object sidewaysFriction = SidewaysFrictionInfo.GetValue(c);

                        GameObject newGameObject = Instantiate(SkidmarkObjectPrefab, transform);
                        Skidmarks newSkidmarks = newGameObject.GetComponent<Skidmarks>();

                        ResizableWheelDataList.Add(new ResizableWheelData(c,
                            forwardFriction,
                            sidewaysFriction,
                            (float)FrictionCurveAsymptoteSlipInfo.GetValue(forwardFriction),
                            (float)FrictionCurveExtremumSlipInfo.GetValue(forwardFriction),
                            (float)FrictionCurveAsymptoteSlipInfo.GetValue(sidewaysFriction),
                            (float)FrictionCurveExtremumSlipInfo.GetValue(sidewaysFriction),
                            (float)FrictionCurveAsymptoteValueInfo.GetValue(forwardFriction),
                            (float)FrictionCurveExtremumValueInfo.GetValue(forwardFriction),
                            (float)FrictionCurveAsymptoteValueInfo.GetValue(sidewaysFriction),
                            (float)FrictionCurveExtremumValueInfo.GetValue(sidewaysFriction),
                            newSkidmarks,
                            c.transform.parent.parent.parent.localScale,
                            c.transform.parent.Find("Wheel").localScale
                            ));

                        Debug.Log($"Added wheel data entry: {ResizableWheelDataList[ResizableWheelDataList.Count - 1]}");
                    }
                }

                int numRemoved = ResizableWheelDataList.RemoveAll(IsNull);
                if (numRemoved > 0)
                {
                    Debug.Log($"EllipseGripScript: Removed {numRemoved} wheel data entries as the components no longer exist.");
                }
            }
        }

        // Update the particle timer
        ParticleTimer -= Time.fixedDeltaTime;
        bool EmitParticles;
        if (!ParticlesEnabled)
        {
            EmitParticles = false;
        }
        else if (ParticleTimer <= 0f)
        {
            EmitParticles = true;
            ParticleTimer = ParticlePeriod;
        }
        else
        {
            EmitParticles = false;
        }

        // Update the skidmarks timer
        SkidmarksTimer -= Time.fixedDeltaTime;
        bool UpdateSkidmarks;
        if (!SkidmarksEnabled)
        {
            UpdateSkidmarks = false;
        }
        else if (SkidmarksTimer <= 0f)
        {
            UpdateSkidmarks = true;
            SkidmarksTimer = SkidmarksPeriod;
        }
        else
        {
            UpdateSkidmarks = false;
        }

        // Physics
        foreach (ResizableWheelData data in ResizableWheelDataList)
        {
            float forwardSlip = (float)ForwardSlipInfo.GetValue(data.collider);
            float sidewaysSlip = (float)SidewaysSlipInfo.GetValue(data.collider);
            float magnitudeOfSlip = Mathf.Sqrt(forwardSlip * forwardSlip + sidewaysSlip * sidewaysSlip);
            float wheelSlipAngle = Mathf.Atan2(sidewaysSlip, forwardSlip);

            // Radius of ellipse at angle
            // https://math.stackexchange.com/questions/432902/

            // Slip that causes an overload factor of 0
            float extremumSlipAtAngle = data.forwardExtremumSlip * data.sidewaysExtremumSlip /
                Mathf.Sqrt(
                    Mathf.Pow(data.forwardExtremumSlip * Mathf.Sin(wheelSlipAngle), 2) +
                    Mathf.Pow(data.sidewaysExtremumSlip * Mathf.Cos(wheelSlipAngle), 2)
                    );

            // Slip that causes an overload factor of 1
            float asymptoteSlipAtAngle = data.forwardAsymptoteSlip * data.sidewaysAsymptoteSlip /
                Mathf.Sqrt(
                    Mathf.Pow(data.forwardAsymptoteSlip * Mathf.Sin(wheelSlipAngle), 2) +
                    Mathf.Pow(data.sidewaysAsymptoteSlip * Mathf.Cos(wheelSlipAngle), 2)
                    );

            // Overload factor in the range 0..1
            float overloadingFactor = Mathf.InverseLerp(extremumSlipAtAngle, asymptoteSlipAtAngle, magnitudeOfSlip);

            const float tangentFrictionMultiplier = 1f;
            float normalFrictionMultiplier = 1f - EffectStrength * overloadingFactor;

            float forwardFrictionMultiplier =
                tangentFrictionMultiplier * Mathf.Abs(Mathf.Pow(Mathf.Cos(wheelSlipAngle), 2)) +
                normalFrictionMultiplier * Mathf.Abs(Mathf.Pow(Mathf.Sin(wheelSlipAngle), 2));
            FrictionCurveAsymptoteValueInfo.SetValue(data.forwardFrictionCurve, data.defaultForwardAsymptoteValue * forwardFrictionMultiplier);
            FrictionCurveExtremumValueInfo.SetValue(data.forwardFrictionCurve, data.defaultForwardExtremumValue * forwardFrictionMultiplier);

            float sidewaysFrictionMultiplier =
                tangentFrictionMultiplier * Mathf.Abs(Mathf.Pow(Mathf.Sin(wheelSlipAngle), 2)) +
                normalFrictionMultiplier * Mathf.Abs(Mathf.Pow(Mathf.Cos(wheelSlipAngle), 2));
            FrictionCurveAsymptoteValueInfo.SetValue(data.sidewaysFrictionCurve, data.defaultSidewaysAsymptoteValue * sidewaysFrictionMultiplier);
            FrictionCurveExtremumValueInfo.SetValue(data.sidewaysFrictionCurve, data.defaultSidewaysExtremumValue * sidewaysFrictionMultiplier);

            // Emit tire smoke particles
            if (overloadingFactor > 0 && EmitParticles)
            {
                OverrideParams.startColor = ParticleColor * new Color(1f, 1f, 1f, Mathf.Pow(overloadingFactor, ParticleAlphaPower));
                data.particleSystem.Emit(OverrideParams, 1);
            }

            // Skidmarks
            if (UpdateSkidmarks)
            {
                if (overloadingFactor > 0)
                {
                    data.LastSkidmarkIndex = data.Skidmarks.AddSkidMark(
                        data.collider.transform.position - data.collider.transform.parent.up * data.WheelRadius + ServiceProvider.Instance.GameWorld.FloatingOriginOffset,
                        (Vector3)SurfaceNormalInfo.GetValue(data.collider),
                        Mathf.Clamp01(overloadingFactor / SkidmarksMaximumOverload) * SkidmarksMaximumOpacity,
                        data.LastSkidmarkIndex
                        );
                }
                else
                {
                    data.LastSkidmarkIndex = -1;
                }
            }

            if (DebugMode)
            {
                DebugText.AppendLine($"{forwardFrictionMultiplier:0.000}, {sidewaysFrictionMultiplier:0.000} ({forwardSlip:00.000}, {sidewaysSlip:00.000})");
            }
        }

        if (DebugMode)
        {
            ServiceProvider.Instance.GameWorld.ShowStatusMessage(DebugText.ToString());
            DebugText.Clear();
        }
    }

    private void OnDestroy()
    {
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_ToggleDebugMode");
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_ToggleParticles");
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_ToggleSkidmarks");
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_SetParticleColor");
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_SetParticleAlphaExponent");
        ServiceProvider.Instance.DevConsole.UnregisterCommand("EllipseGrip_SetStrength");

        // Save settings file
        BinaryWriter writer = new BinaryWriter(File.Create(PathSettings));
        writer.Write(FileHeaderV0);
        writer.Write(DebugMode);
        writer.Write(ParticlesEnabled);
        writer.Write(ParticleColor.r);
        writer.Write(ParticleColor.g);
        writer.Write(ParticleColor.b);
        writer.Write(ParticleColor.a);
        writer.Write(ParticleAlphaPower);
        writer.Write(SkidmarksEnabled);
        writer.Write(EffectStrength);
        writer.Close();
    }

    public void ToggleDebugMode()
    {
        DebugMode = !DebugMode;
    }

    public void ToggleParticles()
    {
        ParticlesEnabled = !ParticlesEnabled;
    }

    public void ToggleSkidmarks()
    {
        SkidmarksEnabled = !SkidmarksEnabled;
    }

    public void SetParticleColor(Color c)
    {
        ParticleColor = c;
    }

    public void SetParticleAlphaExponent(float f)
    {
        ParticleAlphaPower = f;
    }

    public void SetEffectStrength(float strength)
    {
        EffectStrength = strength;
    }

    private static bool IsNull(Component c)
    {
        return c == null;
    }

    private static bool IsNull(ResizableWheelData d)
    {
        return d == null || d.collider == null;
    }

    private class ResizableWheelData
    {
        private const float UNSCALED_WHEEL_THICKNESS = 0.2f;

        public Component collider;
        public object forwardFrictionCurve;
        public object sidewaysFrictionCurve;

        public float forwardAsymptoteSlip;
        public float forwardExtremumSlip;
        public float sidewaysAsymptoteSlip;
        public float sidewaysExtremumSlip;

        public float defaultForwardAsymptoteValue;
        public float defaultForwardExtremumValue;
        public float defaultSidewaysAsymptoteValue;
        public float defaultSidewaysExtremumValue;

        public ParticleSystem particleSystem;

        public Skidmarks Skidmarks;
        public int LastSkidmarkIndex = -1;
        public float WheelThickness;
        public float WheelRadius;

        public ResizableWheelData (Component c, object forwardFrictionCurve, object sidewaysFrictionCurve,
            float forwardAsymptoteSlip, float forwardExtremumSlip, float sidewaysAsymptoteSlip, float sidewaysExtremumSlip,
            float forwardAsymptoteValue, float forwardExtremumValue, float sidewaysAsymptoteValue, float sidewaysExtremumValue,
            Skidmarks skidmarkComponent, Vector3 wheelPartScale, Vector3 wheelLocalScale)
        {
            collider = c;
            this.forwardFrictionCurve = forwardFrictionCurve;
            this.sidewaysFrictionCurve = sidewaysFrictionCurve;

            this.forwardAsymptoteSlip = forwardAsymptoteSlip;
            this.forwardExtremumSlip = forwardExtremumSlip;
            this.sidewaysAsymptoteSlip = sidewaysAsymptoteSlip;
            this.sidewaysExtremumSlip = sidewaysExtremumSlip;

            defaultForwardAsymptoteValue = forwardAsymptoteValue;
            defaultForwardExtremumValue = forwardExtremumValue;
            defaultSidewaysAsymptoteValue = sidewaysAsymptoteValue;
            defaultSidewaysExtremumValue = sidewaysExtremumValue;

            particleSystem = c.transform.parent.parent.parent.gameObject.GetComponentInChildren<ParticleSystem>();

            Skidmarks = skidmarkComponent;

            // Wheel local scale determined in .../WheelRoot/Wheel
            // Y = Z = real radius = size / 4
            // X = size * width
            WheelRadius = wheelPartScale.y * wheelLocalScale.y;
            WheelThickness = wheelPartScale.x * wheelLocalScale.x * UNSCALED_WHEEL_THICKNESS;
            Skidmarks.MarkWidth = WheelThickness;
        }

        public override string ToString()
        {
            return $"{collider} ({forwardAsymptoteSlip}, {forwardExtremumSlip}, {sidewaysAsymptoteSlip}, {sidewaysExtremumSlip})";
        }
    }
}
