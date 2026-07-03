namespace BurnDisc.Model;

//
// A cheap identity for a written disc: track count plus used size in MiB. Both
// come from `drutil status`, so the fingerprint recorded right after a burn
// matches the one computed when that disc is re-inserted later. Heuristic (two
// different games could collide) but more than good enough for a personal library.
//
internal readonly record struct DiscFingerprint(int Tracks, int Mib);
