using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    // Convert each character before insertion, keeping TMP's caret, paste and submit handling.
    [DisallowMultipleComponent]
    public sealed class UppercasePlayerInput : MonoBehaviour
    {
        private TMP_InputField tmp;
        private UnityEngine.UI.InputField legacy;
        private TMP_InputField.OnValidateInput previousTmp;
        private UnityEngine.UI.InputField.OnValidateInput previousLegacy;

        private void Awake()
        {
            tmp = GetComponent<TMP_InputField>();
            legacy = GetComponent<UnityEngine.UI.InputField>();
            if (tmp != null)
            {
                previousTmp = tmp.onValidateInput;
                tmp.onValidateInput = ValidateTmp;
            }
            if (legacy != null)
            {
                previousLegacy = legacy.onValidateInput;
                legacy.onValidateInput = ValidateLegacy;
            }
        }

        private char ValidateTmp(string text, int index, char value)
        {
            if (previousTmp != null) value = previousTmp(text, index, value);
            return char.ToUpperInvariant(value);
        }

        private char ValidateLegacy(string text, int index, char value)
        {
            if (previousLegacy != null) value = previousLegacy(text, index, value);
            // The lobby join-code field must still reject spaces and punctuation.
            if (legacy.characterValidation == UnityEngine.UI.InputField.CharacterValidation.Alphanumeric &&
                !((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
                  (value >= '0' && value <= '9'))) return '\0';
            return char.ToUpperInvariant(value);
        }

        private void OnDestroy()
        {
            if (tmp != null && tmp.onValidateInput == ValidateTmp) tmp.onValidateInput = previousTmp;
            if (legacy != null && legacy.onValidateInput == ValidateLegacy) legacy.onValidateInput = previousLegacy;
        }
    }
}
