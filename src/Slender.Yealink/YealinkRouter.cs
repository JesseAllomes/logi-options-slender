using Slender.Engine;

namespace Slender.Yealink;

/// <summary>
/// Runs the action the user bound to the headset's Hook, Reject and Teams buttons. An unbound button (ActionSpec.None) is left to
/// the phone app: Slender does nothing. The module raises events on its read thread, so the action is handed to <c>post</c>
/// (the thread pool in the tray) and never runs there. Holds no thread or buffer of its own; one log line per press.
/// </summary>
public sealed class YealinkRouter : IDisposable
{
    private readonly YealinkModule _module;
    private readonly IActionExecutor _executor;
    private readonly Func<YealinkSettings> _settings;
    private readonly Action<Action> _post;
    private readonly Action<string> _log;

    public YealinkRouter(YealinkModule module, IActionExecutor executor, Func<YealinkSettings> settings, Action<Action> post, Action<string> log)
    {
        _module = module;
        _executor = executor;
        _settings = settings;
        _post = post;
        _log = log;
        module.HookChanged += OnHook;
        module.RejectPressed += OnReject;
        module.TeamsButtonPressed += OnTeams;
    }

    public void Dispose()
    {
        _module.HookChanged -= OnHook;
        _module.RejectPressed -= OnReject;
        _module.TeamsButtonPressed -= OnTeams;
    }

    // The hook is a switch (off-hook/on-hook); either change counts as one press of the Hook button.
    private void OnHook(bool offHook) => Run("Hook", offHook ? "off-hook" : "on-hook", s => s.Hook);

    private void OnReject() => Run("Reject", "pressed", s => s.Reject);

    private void OnTeams() => Run("Teams button", "pressed", s => s.TeamsButton);

    private void Run(string button, string what, Func<YealinkSettings, ActionSpec> pick)
    {
        var action = pick(_settings());
        if (action is ActionSpec.None)
        {
            _log($"Yealink: {button} {what}; left to the phone app");
            return;
        }

        _log($"Yealink: {button} {what}; running its action");
        _post(() =>
        {
            try
            {
                _executor.Execute(action, true);
                _executor.Execute(action, false);
            }
            catch (Exception ex) { _log($"Yealink: {button} action failed: {ex.Message}"); }
        });
    }
}
