using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public interface IPluginSettingsView
{
 void Render(PluginSettingsState state);

 void ShowMessage(string message);
}
