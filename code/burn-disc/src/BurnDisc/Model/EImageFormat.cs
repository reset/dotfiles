namespace BurnDisc.Model;

//
// The input formats burn-disc understands. Archives are unwrapped first, then
// re-routed to whichever image format they contained.
//
internal enum EImageFormat {
    Archive, // .7z .zip .rar — contains one of the below
    Cue,     // .cue (+ .bin)
    Chd,     // .chd — converted to bin/cue via chdman
    Ccd,     // .ccd (+ .img) — converted to cue natively, burned with --swap
    Iso,     // .iso — single data track, burned with cdrecord
    BinImg   // bare .bin/.img — resolves to a sibling .cue/.ccd
}
