// Explicit assembly attributes for the MinimalProductApi fixture.
// See MinimalProductApi.csproj for why GenerateAssemblyInfo is disabled.
//
// This fixture intentionally declares [AssemblyProduct] and [AssemblyCompany]
// but NOT [AssemblyTitle] and NOT [AssemblyDescription] — so tests can assert
// that:
//   - info.title     falls back to [AssemblyProduct]
//   - info.description stays null (no [AssemblyDescription], no CLI option)
//   - info.contact.name comes from [AssemblyCompany]
[assembly: System.Reflection.AssemblyProduct("Minimal Product")]
[assembly: System.Reflection.AssemblyCompany("Minimal Co")]
