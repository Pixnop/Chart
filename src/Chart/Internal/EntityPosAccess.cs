using System;
using System.Reflection;
using Vintagestory.API.Common.Entities;

namespace Chart.Internal;

/// <summary>
/// Binary-compatibility accessor for <see cref="Entity.Pos"/>.
/// </summary>
/// <remarks>
/// VS 1.22 refactored <see cref="Entity.Pos"/> from a public field into a public property. IL
/// emitted for direct field access against the 1.21 shape throws <see cref="MissingFieldException"/>
/// at runtime against 1.22, and the opposite (callvirt against a 1.21 build) throws
/// <see cref="MissingMethodException"/>. This helper resolves the member once via reflection at
/// type-load time and returns the same <see cref="EntityPos"/> reference either way, so Chart
/// binaries built against one shape keep working on the other.
/// </remarks>
internal static class EntityPosAccess
{
    private static readonly Func<Entity, EntityPos?> Getter = BuildGetter();

    /// <summary>Returns the entity's position. Throws if <paramref name="entity"/> is null.</summary>
    /// <param name="entity">The entity to read from.</param>
    /// <returns>The entity's <see cref="EntityPos"/>.</returns>
    public static EntityPos Pos(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Getter(entity)
            ?? throw new InvalidOperationException("Entity.Pos returned null.");
    }

    /// <summary>Returns the entity's position, or null if the entity or its Pos is null.</summary>
    /// <param name="entity">The entity to read from, or null.</param>
    /// <returns>The entity's <see cref="EntityPos"/>, or null.</returns>
    public static EntityPos? PosOrNull(Entity? entity) => entity is null ? null : Getter(entity);

    private static Func<Entity, EntityPos?> BuildGetter()
    {
        var t = typeof(Entity);
        var field = t.GetField("Pos", BindingFlags.Public | BindingFlags.Instance);
        if (field is not null)
        {
            return e => (EntityPos?)field.GetValue(e);
        }

        var prop = t.GetProperty("Pos", BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null)
        {
            var getMethod = prop.GetGetMethod()
                ?? throw new InvalidOperationException("Entity.Pos has no public getter on this Vintagestory API.");
            return e => (EntityPos?)getMethod.Invoke(e, null);
        }

        throw new InvalidOperationException("Entity.Pos is neither a public field nor a public property on this Vintagestory API.");
    }
}
