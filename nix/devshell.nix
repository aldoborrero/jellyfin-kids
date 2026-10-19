{ pkgs, perSystem }:
let
  dotnet = pkgs.dotnet-sdk_10;
in
perSystem.devshell.mkShell {
  packages = with pkgs; [
    nixfmt-tree # same formatter `nix fmt` uses, for the nix/ files

    # Plugin build: Jellyfin 12.x plugins target net10.0
    dotnet

    # Dev loop: a local Jellyfin 12.x server, rebuilt and restarted on change by process-compose
    jellyfin # wraps --webdir (jellyfin-web) and --ffmpeg (jellyfin-ffmpeg)
    jellyfin-ffmpeg # on PATH for provisioning, which generates the test clip
    process-compose

    # Provisioning and poking at the API
    curl
    jq
    yq-go
  ];

  env = [
    {
      name = "DOTNET_ROOT";
      value = "${dotnet}/share/dotnet";
    }
    {
      name = "DOTNET_CLI_TELEMETRY_OPTOUT";
      value = "1";
    }
    {
      name = "DOTNET_NOLOGO";
      value = "1";
    }
    {
      # Where the dev server keeps its data, config, cache and logs
      name = "JELLYFIN_KIDS_DEV";
      eval = "$PRJ_ROOT/.dev";
    }
  ];

  commands = [
    {
      name = "dev";
      help = "Run the dev loop: Jellyfin with the plugin, rebuilt and reloaded on change";
      command = "process-compose up \"$@\"";
    }
    {
      name = "dev-reset";
      help = "Delete the dev server's state, so the next `dev` provisions it again";
      command = "rm -rf \"$JELLYFIN_KIDS_DEV\"";
    }
  ];
}
