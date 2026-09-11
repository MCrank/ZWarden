// The runtime half of reflection mode (ADR 0002). The build property
// EnableTUnitSourceGeneration=false alone does nothing at run time; this
// attribute is what tells TUnit to discover tests by reflection. Kept in a
// version-controlled .cs file rather than TUNIT_EXECUTION_MODE/--reflection so
// the switch cannot be lost to an environment difference.
[assembly: TUnit.Core.ReflectionMode]
