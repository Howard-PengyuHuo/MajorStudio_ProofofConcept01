using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Services.Vivox;
using UnityEngine;

namespace Game.Multiplayer
{
    // All UI objects are saved in MultiplayerCanvas; this script only binds their behavior.
    public sealed class VoiceSettingsUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private UnityEngine.UI.Button openButton, closeButton, refreshButton;
        [SerializeField] private TMP_Dropdown inputDevice, outputDevice;
        [SerializeField] private UnityEngine.UI.Slider inputVolume, outputVolume, teammateVolume;
        [SerializeField] private TMP_Text status, inputValue, outputValue, teammateValue;
        private VoiceChatManager voice;
        private List<VivoxInputDevice> inputs = new();
        private List<VivoxOutputDevice> outputs = new();
        private bool loading, selectingInput, selectingOutput;

        private void Start()
        {
            voice = FindFirstObjectByType<VoiceChatManager>();
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            refreshButton.onClick.AddListener(RefreshDevices);
            inputDevice.onValueChanged.AddListener(SelectInput);
            outputDevice.onValueChanged.AddListener(SelectOutput);
            inputVolume.onValueChanged.AddListener(InputVolume);
            outputVolume.onValueChanged.AddListener(OutputVolume);
            teammateVolume.onValueChanged.AddListener(TeammateVolume);
            panel.SetActive(false);
            if (voice == null) { openButton.interactable = false; return; }
            inputVolume.SetValueWithoutNotify(voice.InputVolume);
            outputVolume.SetValueWithoutNotify(voice.OutputVolume);
            teammateVolume.SetValueWithoutNotify(voice.TeammateVolume);
            UpdateLabels();
        }

        private void Open() { panel.SetActive(true); RefreshDevices(); }
        private void Close() => panel.SetActive(false);
        private void OnDisable() { if (panel != null) panel.SetActive(false); }

        private async void RefreshDevices()
        {
            if (loading || voice == null) return;
            loading = true;
            inputDevice.interactable = outputDevice.interactable = refreshButton.interactable = false;
            status.text = "Loading audio devices...";
            try
            {
                await voice.PrepareSettingsAsync();
                if (this == null) return;
                var sdk = VivoxService.Instance;
                inputs = sdk.AvailableInputDevices.ToList();
                outputs = sdk.AvailableOutputDevices.ToList();
                inputDevice.ClearOptions();
                outputDevice.ClearOptions();
                inputDevice.AddOptions(inputs.Select(d => d.DeviceName).ToList());
                outputDevice.AddOptions(outputs.Select(d => d.DeviceName).ToList());
                inputDevice.SetValueWithoutNotify(Mathf.Max(0, inputs.FindIndex(d => d.DeviceID == sdk.ActiveInputDevice?.DeviceID)));
                outputDevice.SetValueWithoutNotify(Mathf.Max(0, outputs.FindIndex(d => d.DeviceID == sdk.ActiveOutputDevice?.DeviceID)));
                status.text = "Lobby: both can speak. In game: Hacker speaks, Traveler listens.\nVolume adjustment: -50 to +50; 0 = default.";
            }
            catch (Exception e) { if (this != null) status.text = "Audio settings unavailable: " + e.GetType().Name + ". Try Refresh."; }
            finally
            {
                loading = false;
                if (this != null)
                {
                    inputDevice.interactable = inputs.Count > 0;
                    outputDevice.interactable = outputs.Count > 0;
                    refreshButton.interactable = true;
                }
            }
        }

        private async void SelectInput(int index)
        {
            if (loading || selectingInput || index < 0 || index >= inputs.Count) return;
            selectingInput = true;
            inputDevice.interactable = refreshButton.interactable = false;
            try { await VivoxService.Instance.SetActiveInputDeviceAsync(inputs[index]); voice.SetMicrophoneMuted(voice.MicrophoneMuted); }
            catch (Exception e) { if (this != null) status.text = "Input change failed: " + e.GetType().Name; }
            finally { selectingInput = false; if (this != null) { inputDevice.interactable = true; refreshButton.interactable = true; } }
        }

        private async void SelectOutput(int index)
        {
            if (loading || selectingOutput || index < 0 || index >= outputs.Count) return;
            selectingOutput = true;
            outputDevice.interactable = refreshButton.interactable = false;
            try { await VivoxService.Instance.SetActiveOutputDeviceAsync(outputs[index]); }
            catch (Exception e) { if (this != null) status.text = "Output change failed: " + e.GetType().Name; }
            finally { selectingOutput = false; if (this != null) { outputDevice.interactable = true; refreshButton.interactable = true; } }
        }
        private void InputVolume(float value) { voice.SetInputVolume(value); UpdateLabels(); }
        private void OutputVolume(float value) { voice.SetOutputVolume(value); UpdateLabels(); }
        private void TeammateVolume(float value) { voice.SetTeammateVolume(value); UpdateLabels(); }
        private void UpdateLabels()
        {
            inputValue.text = voice.InputVolume.ToString("+0;-0;0");
            outputValue.text = voice.OutputVolume.ToString("+0;-0;0");
            teammateValue.text = voice.TeammateVolume.ToString("+0;-0;0");
        }
    }
}
