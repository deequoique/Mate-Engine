using UnityEngine;
using VRM;
using UniVRM10;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class UniversalBlendshapes : MonoBehaviour
{
    [Header("Universal Preview")]
    [Range(0f, 1f)] public float Blink, Blink_L, Blink_R, LookUp, LookDown, LookLeft, LookRight, Neutral;
    [Range(0f, 1f)] public float A, I, U, E, O, Joy, Angry, Sorrow, Fun;
    public float fadeSpeed = 5f, safeTimeout = 2f, minHoldTime = 0.1f;

    private VRMBlendShapeProxy proxy0; private Vrm10Instance vrm1; private Vrm10RuntimeExpression expr1;
    private class BlendState { public float value, lastInput, lastUpdateTime, holdUntil; }

    private readonly Dictionary<string, BlendState> states = new();
    private readonly List<KeyValuePair<BlendShapeKey, float>> reusableList = new();

    private static readonly string[] keys = new[]
    {
        "Blink", "Blink_L", "Blink_R",
        "LookUp", "LookDown", "LookLeft", "LookRight",
        "Neutral", "A", "I", "U", "E", "O",
        "Joy", "Angry", "Sorrow", "Fun"
    };

    private static readonly BlendShapePreset[] vrm0Presets = new[]
    {
        BlendShapePreset.Blink, BlendShapePreset.Blink_L, BlendShapePreset.Blink_R,
        BlendShapePreset.LookUp, BlendShapePreset.LookDown, BlendShapePreset.LookLeft, BlendShapePreset.LookRight,
        BlendShapePreset.Neutral,
        BlendShapePreset.A, BlendShapePreset.I, BlendShapePreset.U, BlendShapePreset.E, BlendShapePreset.O,
        BlendShapePreset.Joy, BlendShapePreset.Angry, BlendShapePreset.Sorrow, BlendShapePreset.Fun
    };

    private static readonly Dictionary<string, string> vrm10KeyMap = new()
    {
        { "A", "aa" }, { "I", "ih" }, { "U", "ou" }, { "E", "ee" }, { "O", "oh" },
        { "Joy", "happy" }, { "Angry", "angry" }, { "Sorrow", "sad" }, { "Fun", "relaxed" },
        { "Blink", "blink" }, { "Blink_L", "blinkLeft" }, { "Blink_R", "blinkRight" },
        { "LookUp", "lookUp" }, { "LookDown", "lookDown" }, { "LookLeft", "lookLeft" }, { "LookRight", "lookRight" },
        { "Neutral", "neutral" }
    };

    private readonly Dictionary<string, ExpressionKey> vrm1ExpressionKeyMap = new();
    private readonly float[] valueCache = new float[keys.Length];
    private static readonly string[] surpriseAliases = { "surprised", "Surprised" };
    private readonly Dictionary<string, BlendState> customStates = new();
    private readonly Dictionary<string, float> customImmediateValues = new();

    private void Awake()
    {
        proxy0 = GetComponent<VRMBlendShapeProxy>();
        vrm1 = GetComponentInChildren<Vrm10Instance>(true);
        expr1 = vrm1 != null ? vrm1.Runtime?.Expression : null;

        for (int i = 0; i < keys.Length; i++)
            states[keys[i]] = new BlendState();

        if (expr1 != null)
        {
            vrm1ExpressionKeyMap.Clear();
            foreach (var k in expr1.ExpressionKeys)
            {
                if (!vrm1ExpressionKeyMap.ContainsKey(k.Name))
                    vrm1ExpressionKeyMap[k.Name] = k;
            }
        }
    }

    private void LateUpdate()
    {
        float now = Time.time;
        float dt = Time.deltaTime;

        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            float input = valueCache[i] = GetInputValue(i);
            UpdateState(key, input, now, dt);
        }

        if (proxy0 != null)
        {
            reusableList.Clear();
            for (int i = 0; i < keys.Length; i++)
            {
                reusableList.Add(new KeyValuePair<BlendShapeKey, float>(
                    BlendShapeKey.CreateFromPreset(vrm0Presets[i]), states[keys[i]].value
                ));
            }
            foreach (var pair in customImmediateValues)
            {
                reusableList.Add(new KeyValuePair<BlendShapeKey, float>(
                    BlendShapeKey.CreateUnknown(pair.Key), pair.Value
                ));
            }
            proxy0.SetValues(reusableList);
            proxy0.Apply();
        }
        else if (expr1 != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!vrm10KeyMap.TryGetValue(key, out var mapped)) mapped = key;
                if (vrm1ExpressionKeyMap.TryGetValue(mapped, out var exprKey))
                {
                    expr1.SetWeight(exprKey, states[key].value);
                }
            }

            foreach (var pair in customImmediateValues)
            {
                if (vrm1ExpressionKeyMap.TryGetValue(pair.Key, out var exprKey))
                {
                    expr1.SetWeight(exprKey, pair.Value);
                }
            }
        }
    }

    private float GetInputValue(int i) => i switch
    {
        0 => Blink,
        1 => Blink_L,
        2 => Blink_R,
        3 => LookUp,
        4 => LookDown,
        5 => LookLeft,
        6 => LookRight,
        7 => Neutral,
        8 => A,
        9 => I,
        10 => U,
        11 => E,
        12 => O,
        13 => Joy,
        14 => Angry,
        15 => Sorrow,
        16 => Fun,
        _ => 0f
    };


    private void UpdateState(string key, float input, float now, float dt)
    {
        if (!states.TryGetValue(key, out var state)) return;

        bool changed = !Mathf.Approximately(input, state.lastInput);
        bool activelyDriven = !Mathf.Approximately(input, 0f);

        if (changed || activelyDriven)
        {
            state.lastInput = input;
            state.lastUpdateTime = now;
            state.value = input;
            state.holdUntil = now + minHoldTime;
        }
        else
        {
            if (now < state.holdUntil)
            {
                state.value = input;
            }
            else
            {
                float idleTime = now - state.lastUpdateTime;
                if (idleTime > safeTimeout)
                {
                    state.value = 0f;
                }
                else
                {
                    state.value = Mathf.MoveTowards(state.value, 0f, fadeSpeed * dt);
                }
            }
        }
    }

    public void SetPresetValue(string presetName, float value)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return;
        string normalized = presetName.Trim();
        float clamped = Mathf.Clamp01(value);

        switch (normalized.ToLowerInvariant())
        {
            case "blink": Blink = clamped; return;
            case "blink_l":
            case "blinkleft": Blink_L = clamped; return;
            case "blink_r":
            case "blinkright": Blink_R = clamped; return;
            case "lookup": LookUp = clamped; return;
            case "lookdown": LookDown = clamped; return;
            case "lookleft": LookLeft = clamped; return;
            case "lookright": LookRight = clamped; return;
            case "neutral":
            case "relaxed": Neutral = clamped; return;
            case "a":
            case "aa": A = clamped; return;
            case "i":
            case "ih": I = clamped; return;
            case "u":
            case "ou": U = clamped; return;
            case "e":
            case "ee": E = clamped; return;
            case "o":
            case "oh": O = clamped; return;
            case "joy":
            case "happy": Joy = clamped; return;
            case "angry": Angry = clamped; return;
            case "sorrow":
            case "sad": Sorrow = clamped; return;
            case "fun": Fun = clamped; return;
        }

        SetCustomValue(normalized, clamped);
    }

    public void SetEmotion(string expressionName, float value)
    {
        if (string.IsNullOrWhiteSpace(expressionName)) return;
        float clamped = Mathf.Clamp01(value);
        string normalized = expressionName.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "joy":
            case "happy":
                Joy = clamped;
                break;
            case "sorrow":
            case "sad":
                Sorrow = clamped;
                break;
            case "angry":
                Angry = clamped;
                break;
            case "surprised":
                SetSurprised(clamped);
                break;
            case "neutral":
            case "relaxed":
                Neutral = clamped;
                break;
            case "blink":
                Blink = clamped;
                break;
            case "aa":
            case "a":
                A = clamped;
                break;
            case "ih":
            case "i":
                I = clamped;
                break;
            case "ou":
            case "u":
                U = clamped;
                break;
            case "ee":
            case "e":
                E = clamped;
                break;
            case "oh":
            case "o":
                O = clamped;
                break;
            default:
                SetCustomValue(expressionName.Trim(), clamped);
                break;
        }
    }

    public void ClearEmotion(string expressionName)
    {
        SetEmotion(expressionName, 0f);
    }

    void SetSurprised(float value)
    {
        for (int i = 0; i < surpriseAliases.Length; i++)
        {
            SetCustomValue(surpriseAliases[i], value);
        }
    }

    void SetCustomValue(string key, float value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        string safeKey = key.Trim();
        customImmediateValues[safeKey] = value;

        if (!customStates.TryGetValue(safeKey, out var state))
        {
            state = new BlendState();
            customStates[safeKey] = state;
        }

        state.lastInput = value;
        state.lastUpdateTime = Time.time;
        state.holdUntil = Time.time + minHoldTime;
        state.value = value;
    }
}
