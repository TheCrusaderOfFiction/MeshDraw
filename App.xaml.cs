using System.Windows;
namespace MeshDraw;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Length==2 && e.Args[0]=="--verify-editor")
        {
            base.OnStartup(e);
            try
            {
                var window=new EditorWindow(); await window.RunChecks(System.IO.Path.GetFullPath(e.Args[1])); Shutdown(0);
            }
            catch(Exception error)
            {
                System.IO.Directory.CreateDirectory(e.Args[1]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"editor-checks.txt"),error.ToString()); Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
        new EditorWindow().Show();
    }
}
