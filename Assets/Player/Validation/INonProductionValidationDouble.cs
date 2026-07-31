namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Marks every type in this assembly as a validation-only stand-in for environment geometry the
    /// Environment system owns in production. These components exist so PlayerTestScene can exercise
    /// grounding, obstruction, obstacle, and coin behaviour with zero production environment code
    /// present. They implement Player Controller contracts only, they carry no generation, placement,
    /// scoring, or lifecycle logic, and they must never be referenced as production environment
    /// assets. Production environment objects are supplied by the Environment system and replace
    /// these stand-ins wherever the two are ever present together.
    ///
    /// Each component lives in its own file named after its class. Unity only creates a MonoScript
    /// asset for the class whose name matches the file name, so a MonoBehaviour sharing a file with
    /// others cannot be referenced from a scene or prefab at all.
    /// </summary>
    public interface INonProductionValidationDouble
    {
        /// <summary>Human-readable reason this object is not production content.</summary>
        string NonProductionNotice { get; }
    }
}
