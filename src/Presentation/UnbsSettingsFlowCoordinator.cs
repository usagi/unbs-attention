#if UNBS_BSIPA
using BeatSaberMarkupLanguage;
using HMUI;

namespace UnbsAttention.Presentation;

public sealed class UnbsSettingsFlowCoordinator : FlowCoordinator
{
 public Action? OnBackButton;

 private UnbsSettingsLeftViewController? _leftViewController;
 private UnbsSettingsViewController? _settingsViewController;
 private MainFlowCoordinator? _mainFlowCoordinator;

 public void Bind(UnbsSettingsLeftViewController leftViewController, UnbsSettingsViewController settingsViewController)
 {
  _leftViewController = leftViewController;
  _settingsViewController = settingsViewController;
 }

 public void BindMainFlow(MainFlowCoordinator? mainFlowCoordinator)
 {
  _mainFlowCoordinator = mainFlowCoordinator;
 }

 protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
 {
  if (firstActivation)
  {
    SetTitle("UNBS Attention 設定");
  }

    showBackButton = true;

  if (addedToHierarchy && _settingsViewController is not null && _leftViewController is not null)
  {
   ProvideInitialViewControllers(_settingsViewController, _leftViewController, null, null, null);
  }
 }

 protected override void BackButtonWasPressed(ViewController topViewController)
 {
  OnBackButton?.Invoke();
  _mainFlowCoordinator?.DismissFlowCoordinator(this, ViewController.AnimationDirection.Horizontal, null, false);
 }
}
#endif
