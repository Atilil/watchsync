{
  description = "WatchSync Jellyfin plugin";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-26.05";

  outputs = {nixpkgs, ...}: let
    systems = ["x86_64-linux" "aarch64-linux"];
    forAllSystems = f:
      nixpkgs.lib.genAttrs systems (system:
        f (import nixpkgs {
          inherit system;
        }));
  in {
    devShells = forAllSystems (pkgs: {
      default = pkgs.mkShell {
        packages = with pkgs; [
          dotnet-sdk_10
          gitleaks
          just
          ffmpeg
          yq-go
          zip
          git-cliff
          pre-commit
          actionlint
          shellcheck
          markdownlint-cli2
        ];

        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        DOTNET_NOLOGO = "1";
      };
    });
  };
}
