namespace BurnDisc.Model;

//
// The console a disc targets. Detected authoritatively from the data track's
// system-area signature, or guessed from the library folder for the browser.
//
internal enum EPlatform {
    SegaCd,
    Saturn,
    PlayStation,
    Dreamcast,
    DataDisc,
    Unknown
}
