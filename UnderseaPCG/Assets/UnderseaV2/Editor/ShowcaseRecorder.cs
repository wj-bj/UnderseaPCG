using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

// Records short clips of the main camera with Unity Recorder, from the same fixed
// viewpoints ShowcaseStages uses for its screenshots. Play mode only.
public static class ShowcaseRecorder
{
    static RecorderController controller;
    static RecorderControllerSettings controllerSettings;
    static float startedAt;

    // Parks the main camera on one of the ShowcaseStages viewpoints and stops the
    // gameplay scripts from moving it.
    public static string SetView(string shotName)
    {
        if (!Application.isPlaying) return "enter Play mode first";
        var cam = Camera.main;
        var shot = GameObject.Find(shotName);
        if (cam == null || shot == null) return "camera or viewpoint not found";

        foreach (var behaviour in cam.GetComponents<MonoBehaviour>())
        {
            if (behaviour is CameraController || behaviour is CamFollow) behaviour.enabled = false;
        }
        cam.transform.SetPositionAndRotation(shot.transform.position, shot.transform.rotation);
        cam.fieldOfView = 55f;
        cam.farClipPlane = 150f;
        return "view " + shotName;
    }

    public static string StartClip(string outputPathWithoutExtension)
    {
        if (!Application.isPlaying) return "enter Play mode first";
        if (controller != null && controller.IsRecording()) return "already recording";

        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Showcase Clip";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium
        };
        movie.CaptureAudio = false;
        movie.ImageInputSettings = new CameraInputSettings
        {
            Source = ImageSource.MainCamera,
            OutputWidth = 1280,
            OutputHeight = 720,
            CaptureUI = false
        };
        movie.OutputFile = outputPathWithoutExtension;

        controllerSettings.AddRecorderSettings(movie);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30f;
        controllerSettings.CapFrameRate = true;

        controller = new RecorderController(controllerSettings);
        controller.PrepareRecording();
        bool ok = controller.StartRecording();
        startedAt = Time.time;
        return ok ? "recording " + outputPathWithoutExtension : "recorder failed to start";
    }

    // Game-time seconds recorded so far (the recorder fixes the frame rate, so this is clip length).
    public static float Elapsed()
    {
        return controller != null && controller.IsRecording() ? Time.time - startedAt : -1f;
    }

    public static string StopClip()
    {
        if (controller == null) return "not recording";
        float length = Time.time - startedAt;
        controller.StopRecording();
        controller = null;
        return "stopped after " + length.ToString("F1") + "s";
    }
}
