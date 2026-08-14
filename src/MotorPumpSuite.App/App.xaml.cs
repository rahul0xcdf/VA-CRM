namespace MotorPumpSuite.App;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
/// <remarks>
/// The base type is spelled out in full: inside the <c>MotorPumpSuite.App</c> namespace an
/// unqualified <c>Application</c> binds to the <c>MotorPumpSuite.Application</c> namespace, not to
/// the WPF type, and the build fails with CS0118.
/// </remarks>
public partial class App : System.Windows.Application
{
}
