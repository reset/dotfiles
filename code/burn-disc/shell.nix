# Reproducible dev shell for burn-disc.
#
# NOTE: authored to match the project's dependency set but not evaluated on this
# machine (primary dev is macOS/brew). `cdrtools` and `unrar` are unfree — you
# may need `NIXPKGS_ALLOW_UNFREE=1` or an allowUnfree config to enter the shell.
{ pkgs ? import <nixpkgs> { } }:

pkgs.mkShell {
  # Toolchain to build burn-disc, plus the external programs it drives at runtime.
  buildInputs = [
    pkgs.dotnetCorePackages.sdk_10_0
    pkgs.cdrdao   # burn bin/cue
    pkgs.cdrtools # cdrecord — burn ISO
    pkgs.p7zip    # .7z extraction
    pkgs.unrar    # .rar extraction
    # chdman ships inside the (large) `mame` package; uncomment if you burn CHDs:
    # pkgs.mame
  ];
}
