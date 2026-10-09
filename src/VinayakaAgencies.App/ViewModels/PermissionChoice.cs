using CommunityToolkit.Mvvm.ComponentModel;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// One permission a role may or may not grant, as the editor shows it.
/// </summary>
/// <remarks>
/// The label is produced from the enum member name rather than held in a parallel table. A table
/// would be a second list to keep in step, and the failure when it drifts is a tick-box labelled
/// as one right that grants another.
/// </remarks>
public sealed partial class PermissionChoice : ObservableObject
{
    public PermissionChoice(PermissionKind permission, bool isGranted)
    {
        Permission = permission;
        IsGranted = isGranted;
    }

    public PermissionKind Permission { get; }

    /// <summary>"ManageUsers" shown as "Manage users".</summary>
    public string Label => Humanise(Permission.ToString());

    [ObservableProperty]
    private bool _isGranted;

    private static string Humanise(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(name[i]));
                continue;
            }

            text.Append(name[i]);
        }

        return text.ToString();
    }
}

