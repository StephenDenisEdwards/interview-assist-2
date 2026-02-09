using Terminal.Gui;

namespace InterviewAssist.AnnotationConsole;

public sealed class AnnotationApp
{
    private readonly string _transcript;
    private readonly string _fileName;

    public AnnotationApp(string transcript, string fileName)
    {
        _transcript = transcript;
        _fileName = fileName;
    }

    public void Run()
    {
        var mainWindow = new Window($"Transcript: {_fileName}")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
            Text = string.IsNullOrWhiteSpace(_transcript)
                ? "(no transcript content found)"
                : _transcript
        };

        mainWindow.Add(textView);

        var statusBar = new StatusBar(new StatusItem[]
        {
            new(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
        });

        Application.Top.Add(mainWindow, statusBar);
        Application.Run();
    }
}
