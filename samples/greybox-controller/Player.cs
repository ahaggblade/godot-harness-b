using Godot;

public partial class Player : CharacterBody2D
{
    [Export]
    public float MoveSpeed { get; set; } = 240.0f;

    [Export]
    public float JumpVelocity { get; set; } = -420.0f;

    [Export]
    public float GravityScale { get; set; } = 1.0f;

    private float _gravity = (float)ProjectSettings.GetSetting("physics/2d/default_gravity");
    private Vector2 _spawnPosition;
    private bool _sawJumpPress;
    private bool _leftGroundAfterJump;
    private float _furthestX;

    public override void _Ready()
    {
        _spawnPosition = GlobalPosition;
        _furthestX = _spawnPosition.X;
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;
        if (!IsOnFloor())
        {
            velocity.Y += _gravity * GravityScale * (float)delta;
        }

        var direction = Input.GetAxis("move_left", "move_right");
        velocity.X = direction * MoveSpeed;

        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
            _sawJumpPress = true;
        }

        Velocity = velocity;
        MoveAndSlide();

        if (_sawJumpPress && !IsOnFloor())
        {
            _leftGroundAfterJump = true;
        }

        _furthestX = Mathf.Max(_furthestX, GlobalPosition.X);
    }

    public void ResetToSpawn()
    {
        GlobalPosition = _spawnPosition;
        Velocity = Vector2.Zero;
        _sawJumpPress = false;
        _leftGroundAfterJump = false;
        _furthestX = _spawnPosition.X;
    }

    public Godot.Collections.Dictionary<string, Variant> GetSemanticState()
    {
        var distanceMoved = _furthestX - _spawnPosition.X;
        return new Godot.Collections.Dictionary<string, Variant>
        {
            ["spawnX"] = _spawnPosition.X,
            ["spawnY"] = _spawnPosition.Y,
            ["playerX"] = GlobalPosition.X,
            ["playerY"] = GlobalPosition.Y,
            ["distanceMovedRight"] = distanceMoved,
            ["movedRightEnough"] = distanceMoved >= 80.0f,
            ["jumped"] = _leftGroundAfterJump,
            ["onFloor"] = IsOnFloor(),
        };
    }
}

