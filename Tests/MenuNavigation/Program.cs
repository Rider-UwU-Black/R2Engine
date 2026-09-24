using R2Engine.Editor.Scene;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Scene scene = new("Menus");
Canvas MakeCanvas(string name, bool visible)
{
    var c = scene.CreateGameObject(name).AddComponent<Canvas>();
    c.IsVisible = visible;
    return c;
}
UIButton MakeButton(Canvas canvas, string text, UIButtonAction action, string target = "")
{
    var o = scene.CreateGameObject(text);
    o.SetParent(canvas.GameObject, false);
    var b = o.AddComponent<UIButton>();
    b.Action = action; b.TargetCanvas = target;
    return b;
}
var main = MakeCanvas("Main", true);
main.CloseWithCancelInput = false;
var settings = MakeCanvas("Settings", false);
var audio = MakeCanvas("Audio", false);
var play = MakeButton(main, "Play", UIButtonAction.None);
var open = MakeButton(main, "SettingsButton", UIButtonAction.OpenSubmenu, "Settings");
var detail = MakeButton(settings, "AudioButton", UIButtonAction.OpenSubmenu, "Audio");
var back = MakeButton(audio, "Back", UIButtonAction.GoBack);
Canvas.ApplyCanvasAction(scene, open);
Check(!main.IsVisible && settings.IsVisible, "Submenu replaces parent");
Check(scene.MenuNavigation.ResolveFocus(new[] { detail }, 9) == 0, "New submenu starts at first button");
Canvas.ApplyCanvasAction(scene, detail);
Check(!settings.IsVisible && audio.IsVisible, "Second nesting level");
Check(!scene.MenuNavigation.Open(audio, main, back), "Reject ancestor cycles");
Canvas.ApplyCanvasAction(scene, back);
Check(settings.IsVisible && !audio.IsVisible && !main.IsVisible, "Back pops one level");
Check(scene.MenuNavigation.ResolveFocus(new[] { detail }, 0) == 0, "Restore intermediate focus");
scene.MenuNavigation.Cancel(settings);
Check(main.IsVisible && !settings.IsVisible, "Circle returns to root");
Check(scene.MenuNavigation.ResolveFocus(new[] { play, open }, 0) == 1, "Restore Settings focus rather than first button");
scene.MenuNavigation.Cancel(main);
Check(main.IsVisible, "Root main menu remains protected");
Canvas.ApplyCanvasAction(scene, open);
scene.MenuNavigation.Cancel(null);
Check(main.IsVisible, "Back works without a focused child button");
Canvas.ApplyCanvasAction(scene, open);
settings.CloseWithCancelInput = false;
scene.MenuNavigation.Cancel(settings);
Check(settings.IsVisible, "Respect explicit cancel lock");
Check(new Scene("Next").MenuNavigation.Current == null, "New scene has no history");
Console.WriteLine("PASS: nested navigation, one-level back, focus restoration, root protection, empty submenu, cancel lock and scene isolation.");
