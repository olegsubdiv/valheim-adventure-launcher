using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velopack;

namespace AdventureLauncher.Desktop;

public partial class App : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();
            var app = new App();
            app.InitializeComponent();
            if (args.Contains("--self-test"))
            {
                // Parse the compiled XAML and lay out an invisible window; no network, profile writes or game launch.
                var window = new MainWindow(selfTest: true);
                window.VerifyTextureChoicesForSelfTest();
                window.Measure(new Size(980, 940));
                window.Arrange(new Rect(0, 0, 980, 940));
                window.UpdateLayout();
                var report = args.SkipWhile(a => a != "--self-test").Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(report))
                {
                    var directory = Path.GetDirectoryName(Path.GetFullPath(report))!;
                    RenderPreview(window, 980, 940, Path.Combine(directory, "desktop-preview.png"));
                    RenderPreview(window, 820, 780, Path.Combine(directory, "desktop-preview-minimum.png"));
                    File.WriteAllText(report, "Desktop self-test passed: WPF resources, release settings and invisible window initialized. Offscreen previews rendered at 980x940 and 820x780 using synthetic paths.", Encoding.UTF8);
                }
                window.Close();
                return 0;
            }
            app.DispatcherUnhandledException += (_, e) =>
            {
                WriteFailure(e.Exception);
                MessageBox.Show("The launcher encountered an error. Please collect diagnostics.\n\n" + e.Exception.Message,
                    "Adventure RPG", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            return app.Run(new MainWindow());
        }
        catch (Exception ex)
        {
            WriteFailure(ex);
            if (!args.Contains("--self-test"))
                MessageBox.Show("The launcher could not start.\n\n" + ex.Message, "Adventure RPG", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static void RenderPreview(MainWindow window, int width, int height, string path)
    {
        var content = (FrameworkElement)window.Content;
        window.Width = width;
        window.Height = height;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteFailure(Exception ex)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValheimAdventureLauncher", "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "desktop-error.log");
            var text = DateTimeOffset.Now + Environment.NewLine + ex;
            if (text.Length > 64 * 1024) text = text[..(64 * 1024)];
            File.WriteAllText(path, text, Encoding.UTF8);
        }
        catch { /* A logging failure must not hide the startup error. */ }
    }
}
