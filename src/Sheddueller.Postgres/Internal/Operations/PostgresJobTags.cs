namespace Sheddueller.Postgres.Internal.Operations;

using Npgsql;

internal static class PostgresJobTags
{
    public static async ValueTask ReplaceJobTagsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        IReadOnlyList<JobTag>? tags,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeTags(tags);
        await PostgresOperationContext.ExecuteCountAsync(
          connection,
          transaction,
          $"delete from {context.Names.JobTags} where job_id = @job_id;",
          command => command.Parameters.AddWithValue("job_id", jobId),
          cancellationToken)
          .ConfigureAwait(false);

        for (var i = 0; i < normalized.Count; i++)
        {
            var tag = normalized[i];
            await PostgresOperationContext.ExecuteCountAsync(
              connection,
              transaction,
              $"""
              insert into {context.Names.JobTags} (job_id, ordinal, name, value)
              values (@job_id, @ordinal, @name, @value)
              on conflict (job_id, name, value) do nothing;
              """,
              command =>
              {
                  command.Parameters.AddWithValue("job_id", jobId);
                  command.Parameters.AddWithValue("ordinal", i);
                  command.Parameters.AddWithValue("name", tag.Name);
                  command.Parameters.AddWithValue("value", tag.Value);
              },
              cancellationToken)
              .ConfigureAwait(false);
        }
    }

    public static async ValueTask<IReadOnlyList<JobTag>> ReadJobTagsAsync(
        PostgresOperationContext context,
        NpgsqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
          $"""
          select name, value
          from {context.Names.JobTags}
          where job_id = @job_id
          order by ordinal asc, name asc, value asc;
          """;
        command.Parameters.AddWithValue("job_id", jobId);

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
