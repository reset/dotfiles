namespace BurnDisc.Model;

//
// Where a library item lives. Local items burn straight from disk; server
// items are downloaded to a temp dir first.
//
internal enum ELibrarySource {
    Local,
    Server
}
