namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

internal static class PostgresTaskTags
{
    public static async ValueTask ReplaceTaskTagsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        IReadOnlyList<JobTag>? tags,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeTags(tags);
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"delete from {context.Names.TaskTags} where task_id = @task_id;",
          command => command.Parameters.AddWithValue("task_id", taskId),
          cancellationToken)
          .ConfigureAwait(false);

        foreach (var tag in normalized)
        {
            await PostgresOperationContext.ExecuteCountAsync(
              connection,
              transaction,
              $"""
              insert into {context.Names.TaskTags} (task_id, name, value)
              values (@task_id, @name, @value)
              on conflict (task_id, name, value) do nothing;
              """,
              command =>
              {
                  command.Parameters.AddWithValue("task_id", taskId);
                  command.Parameters.AddWithValue("name", tag.Name);
                  command.Parameters.AddWithValue("value", tag.Value);
              },
              cancellationToken)
              .ConfigureAwait(false);
        }
    }

    public static async ValueTask<IReadOnlyList<JobTag>> ReadTaskTagsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select name, value
          from {context.Names.TaskTags}
          where task_id = @task_id
          order by name asc, value asc;
          """;
        command.Parameters.AddWithValue("task_id", taskId);

        var tags = new List<JobTag>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tags.Add(new JobTag(reader.GetString(0), reader.GetString(1)));
        }

        return tags;
    }

    private static List<JobTag> NormalizeTags(IReadOnlyList<JobTag>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<JobTag>();
        var normalized = new List<JobTag>(tags.Count);
        foreach (var tag in tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            var name = tag.Name.Trim();
            var value = tag.Value.Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("Job tag names must be non-empty after trimming.", nameof(tags));
            }

            if (value.Length == 0)
            {
                throw new ArgumentException("Job tag values must be non-empty after trimming.", nameof(tags));
            }

            var normalizedTag = new JobTag(name, value);
            if (seen.Add(normalizedTag))
            {
                normalized.Add(normalizedTag);
            }
        }

        return normalized;
    }
}
