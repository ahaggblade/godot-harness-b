extends Control

func _ready() -> void:
    print("CLI_ARGS=", OS.get_cmdline_args())
    print("CLI_USER_ARGS=", OS.get_cmdline_user_args())

