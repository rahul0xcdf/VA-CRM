using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// One screen a module contributes to the shell: its menu caption, how its view model is built,
/// and the view that renders it.
/// </summary>
/// <remarks>
/// The view-model factory receives the shell's service provider so a screen can take any
/// application service without the shell knowing which. The view is instantiated by the shell and
/// given the view model as its <c>DataContext</c>; views stay free of code-behind logic.
/// </remarks>
public sealed record ScreenDescriptor(
    string Header,
    Func<IServiceProvider, ObservableObject> CreateViewModel,
    Type ViewType);
