using Godot;

public partial class Main : Control
{
    [Export]
    public Button? StartButton { get; set; }

    [Export]
    public Button? OptionsButton { get; set; }

    [Export]
    public Button? QuitButton { get; set; }

    [Export]
    public Label? InfoLabel { get; set; }

    [Export]
    public Label? StatusLabel { get; set; }

    private Button[] _buttons = [];
    private string _lastActivated = "none";

    public override void _Ready()
    {
        StartButton ??= GetNodeOrNull<Button>("StartButton");
        OptionsButton ??= GetNodeOrNull<Button>("OptionsButton");
        QuitButton ??= GetNodeOrNull<Button>("QuitButton");
        InfoLabel ??= GetNodeOrNull<Label>("InfoLabel");
        StatusLabel ??= GetNodeOrNull<Label>("StatusLabel");

        var buttons = new List<Button>();
        if (StartButton is not null)
        {
            buttons.Add(StartButton);
        }

        if (OptionsButton is not null)
        {
            buttons.Add(OptionsButton);
        }

        if (QuitButton is not null)
        {
            buttons.Add(QuitButton);
        }

        _buttons = buttons.ToArray();

        EnsureInputAction("ui_up", Key.Up, Key.W);
        EnsureInputAction("ui_down", Key.Down, Key.S);
        EnsureInputAction("ui_accept", Key.Enter, Key.Space);

        ConfigureButton(StartButton, "Start selected");
        ConfigureButton(OptionsButton, "Options selected");
        ConfigureButton(QuitButton, "Quit selected");
        ConfigureFocusLoop();
        ResetMenu();
    }

    public override void _Process(double delta)
    {
        RefreshVisualState();
    }

    public Godot.Collections.Dictionary<string, Variant> ResetMenu()
    {
        _lastActivated = "none";
        if (StatusLabel is not null)
        {
            StatusLabel.Text = "Waiting for input";
        }

        if (StartButton is not null)
        {
            StartButton.GrabFocus();
        }

        RefreshVisualState();
        return GetMenuState();
    }

    public Godot.Collections.Dictionary<string, Variant> GetMenuState()
    {
        var focused = GetViewport().GuiGetFocusOwner();
        var hovered = GetViewport().GuiGetHoveredControl();
        return new Godot.Collections.Dictionary<string, Variant>
        {
            ["focusedName"] = focused?.Name.ToString() ?? string.Empty,
            ["hoveredName"] = hovered?.Name.ToString() ?? string.Empty,
            ["lastActivated"] = _lastActivated,
            ["statusText"] = StatusLabel?.Text ?? string.Empty,
            ["infoText"] = InfoLabel?.Text ?? string.Empty,
        };
    }

    private void ConfigureButton(Button? button, string statusText)
    {
        if (button is null)
        {
            return;
        }

        button.FocusMode = FocusModeEnum.All;
        button.MouseDefaultCursorShape = CursorShape.PointingHand;
        button.Pressed += () => ActivateButton(button.Name.ToString(), statusText);
    }

    private void ConfigureFocusLoop()
    {
        if (StartButton is null || OptionsButton is null || QuitButton is null)
        {
            return;
        }

        StartButton.FocusNeighborBottom = StartButton.GetPathTo(OptionsButton);
        StartButton.FocusNeighborTop = StartButton.GetPathTo(QuitButton);

        OptionsButton.FocusNeighborTop = OptionsButton.GetPathTo(StartButton);
        OptionsButton.FocusNeighborBottom = OptionsButton.GetPathTo(QuitButton);

        QuitButton.FocusNeighborTop = QuitButton.GetPathTo(OptionsButton);
        QuitButton.FocusNeighborBottom = QuitButton.GetPathTo(StartButton);
    }

    private void ActivateButton(string buttonName, string statusText)
    {
        _lastActivated = buttonName switch
        {
            "StartButton" => "start",
            "OptionsButton" => "options",
            "QuitButton" => "quit",
            _ => buttonName,
        };

        if (StatusLabel is not null)
        {
            StatusLabel.Text = statusText;
        }
    }

    private void RefreshVisualState()
    {
        var focused = GetViewport().GuiGetFocusOwner();
        var hovered = GetViewport().GuiGetHoveredControl();

        foreach (var button in _buttons)
        {
            var isFocused = ReferenceEquals(button, focused);
            var isHovered = ReferenceEquals(button, hovered);
            var background = isFocused
                ? new Color("f4a261")
                : isHovered
                    ? new Color("2a9d8f")
                    : new Color("273043");
            var border = isFocused ? new Color("fff1d6") : new Color("8190a5");
            var text = isFocused || isHovered ? Colors.Black : new Color("f7f4ea");
            ApplyButtonTheme(button, background, border, text);
        }

        if (InfoLabel is not null)
        {
            InfoLabel.Text = $"Focus: {NodeName(focused)}   Hover: {NodeName(hovered)}";
        }
    }

    private static void ApplyButtonTheme(Button button, Color background, Color border, Color text)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 18,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18,
            ContentMarginLeft = 18,
            ContentMarginTop = 12,
            ContentMarginRight = 18,
            ContentMarginBottom = 12,
        };

        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("hover", (StyleBox)style.Duplicate());
        button.AddThemeStyleboxOverride("focus", (StyleBox)style.Duplicate());
        button.AddThemeStyleboxOverride("pressed", (StyleBox)style.Duplicate());
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", text);
        button.AddThemeColorOverride("font_focus_color", text);
        button.AddThemeColorOverride("font_pressed_color", text);
    }

    private static string NodeName(Node? node) => node?.Name.ToString() ?? "None";

    private static void EnsureInputAction(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        foreach (var key in keys)
        {
            var inputEvent = new InputEventKey
            {
                Keycode = key,
            };

            if (!InputMap.ActionHasEvent(action, inputEvent))
            {
                InputMap.ActionAddEvent(action, inputEvent);
            }
        }
    }
}
