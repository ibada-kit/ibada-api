using Azure;
using Azure.Data.Tables;
using System.Linq.Expressions;

namespace ML.Charity.API.Client.Services;
public class TableStorageService<T> : ITableStorageService<T> where T : class, ITableEntity, new()
{
    private readonly TableClient _tableClient;

    public TableStorageService(TableServiceClient serviceClient, string tableName)
    {
        _tableClient = serviceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();
    }

    public async Task AddAsync(T entity)
    {
        try
        {
            await _tableClient.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex)
        {
            throw new Exception($"Failed to insert entity into table {_tableClient.Name}: {ex.Message}", ex);
        }
    }

    public async Task UpdateAsync(T entity)
    {
        try
        {
            // TableUpdateMode.Replace overwrites the entire entity, which is standard for updates here
            await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
        }
        catch (RequestFailedException ex)
        {
            throw new Exception($"Failed to update entity in table {_tableClient.Name}: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(string partitionKey, string rowKey)
    {
        try
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }
        catch (RequestFailedException ex) when (ex.Status != 404)
        {
            throw new Exception($"Failed to delete entity from table {_tableClient.Name}: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<T>> QueryAsync(Expression<Func<T, bool>> filter)
    {
        var entities = new List<T>();

        // This returns Azure's AsyncPageable, which CANNOT be awaited directly
        var queryResults = _tableClient.QueryAsync<T>(filter);

        // We must iterate through the pages asynchronously to extract the items
        await foreach (var item in queryResults)
        {
            entities.Add(item);
        }

        return entities;
    }

    public async Task<T> AddEntityAsync(T entity)
    {
        await AddAsync(entity);
        return entity;
    }

    public async Task<T?> GetEntityAsync(string partitionKey, string rowKey)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<T>> QueryEntitiesByPartitionAsync(string partitionKey)
    {
        var filter = $"PartitionKey eq '{partitionKey}'";
        return await QueryEntitiesAsync(filter);
    }

    public async Task<List<T>> QueryEntitiesAsync(string? filter)
    {
        var entities = new List<T>();
        var queryResults = _tableClient.QueryAsync<T>(filter: filter);

        await foreach (var item in queryResults)
        {
            entities.Add(item);
        }

        return entities;
    }
}