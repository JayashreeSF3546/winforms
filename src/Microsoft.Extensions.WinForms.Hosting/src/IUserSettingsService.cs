// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns explicit asynchronous operations on one application-specific user settings model.
/// </summary>
/// <typeparam name="T">The JSON object model.</typeparam>
/// <remarks>
///  <para>
///   Loads do not migrate or recover implicitly. Save and reset require compatible, valid existing data.
///   Cancellation before atomic replacement aborts a write; cancellation after replacement cannot undo it.
///   Await returned notification completion separately from persistence and never inside a change handler.
///  </para>
/// </remarks>
public interface IUserSettingsService<T> where T : class
{
    /// <summary>
    ///  Occurs for committed writes in instance revision order, without a UI-thread guarantee.
    /// </summary>
    event EventHandler<UserSettingsChangedEventArgs>? Changed;

    /// <summary>
    ///  Loads compatible data, or fresh defaults only when the primary store is absent.
    /// </summary>
    Task<T> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Inspects the persisted schema version, including newer schemas, or null for an absent store.
    /// </summary>
    Task<int?> GetSchemaVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Validates and atomically commits a complete model without merging concurrent edits.
    /// </summary>
    Task<UserSettingsCommit> SaveAsync(T settings, CancellationToken cancellationToken = default);

    /// <summary>
    ///  Explicitly writes fresh defaults without silently replacing corrupt or incompatible data.
    /// </summary>
    Task<UserSettingsCommit> ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Migrates an existing older document and validates its typed model before committing.
    /// </summary>
    Task<UserSettingsCommit> UpgradeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Explicitly restores a compatible, validated backup and preserves the damaged primary.
    /// </summary>
    Task<UserSettingsCommit> RecoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Imports an explicitly mapped legacy model only if no modern primary or backup exists.
    /// </summary>
    /// <param name="readLegacySettings">A caller-selected mapping of user-scoped legacy values.</param>
    /// <param name="cancellationToken">Cancellation before the commit boundary.</param>
    /// <returns>The commit, or null when a modern store already exists.</returns>
    Task<UserSettingsCommit?> ImportAsync(Func<T> readLegacySettings, CancellationToken cancellationToken = default);
}
