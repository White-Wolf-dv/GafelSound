namespace GafelSound;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var engine = new MultiAudioEngine();
        engine.StartEngine();
        Application.Run(new MainForm(engine));
    }
}
