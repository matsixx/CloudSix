using System.Runtime.CompilerServices;

// The CloudSix-FikaSync companion DLL reads/writes CloudSix's internal cloud state
// (CustomCloudController, CloudRenderer) directly. Expose internals to just that assembly so the
// classes can stay internal to everyone else.
[assembly: InternalsVisibleTo("CloudSix-FikaSync")]
