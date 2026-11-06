{
  flake,
  inputs,
  pkgs,
  ...
}:
let
  # Whitespace only, from .editorconfig: needs no restore, so it also runs in the sandboxed check
  dotnet-format = pkgs.writeShellApplication {
    name = "dotnet-format-whitespace";
    runtimeInputs = [ pkgs.dotnet-sdk_10 ];
    text = ''
      export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
      [[ -w ''${HOME:-/nonexistent} ]] || export DOTNET_CLI_HOME=''${TMPDIR:-/tmp}
      exec dotnet format whitespace --folder . --include "$@"
    '';
  };

  mod = inputs.treefmt-nix.lib.evalModule pkgs {
    projectRootFile = "flake.nix";

    settings.excludes = [
      ".specify/**" # vendored spec-kit
      ".claude/**"
      "*.png"
      "*.svg"
    ];

    programs = {
      deadnix.enable = true;
      deadnix.no-lambda-pattern-names = true;
      deadnix.priority = 1;
      nixfmt.enable = true;
      nixfmt.priority = 2;

      shfmt.enable = true;
      shfmt.includes = [
        "*.sh"
        ".envrc"
      ];
      shfmt.indent_size = 2;
      shfmt.simplify = false;

      shellcheck.enable = true;
      shellcheck.includes = [
        "*.sh"
        ".envrc"
      ];

      # YAML only: on the dashboard JS and HTML it rewrites most lines
      prettier.enable = true;
      prettier.includes = [
        "*.yml"
        "*.yaml"
      ];
    };

    settings.formatter.shfmt.options = [
      "-ci" # indent case branches
      "-bn" # keep && and || at the start of continued lines
    ];

    settings.formatter.dotnet-format = {
      command = pkgs.lib.getExe dotnet-format;
      includes = [ "*.cs" ];
    };
  };
  wrapper = mod.config.build.wrapper;
in
wrapper
// {
  passthru = wrapper.passthru // {
    tests.check = mod.config.build.check flake;
  };
}
