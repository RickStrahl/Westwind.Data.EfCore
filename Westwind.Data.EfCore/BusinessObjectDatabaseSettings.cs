using Microsoft.EntityFrameworkCore;

namespace Westwind.Data.EfCore;

public class BusinessObjectDatabaseSettings<TEntity>
    where TEntity : class, new()
{
    private readonly DbContext Context;


    public BusinessObjectDatabaseSettings(DbContext context)
    {
        Context = context;            
    }

    /// <summary>
    /// Optional value for the connection string
    /// </summary>
    public string ConnectionString
    {
        get
        {
            if (string.IsNullOrEmpty(_connectionString) && Context != null)
            {
                var conn = Context.Database.GetDbConnection();
                _connectionString = conn?.ConnectionString;
            }

            return _connectionString;
        }
        set { _connectionString = value; }
    }
    private string _connectionString;

    private static string _tableName;

    /// <summary>
    /// Internally re-usable DbSet instance.
    /// </summary>
    public DbSet<TEntity> DbSet => Context.Set<TEntity>();

        
    /// <summary>
    /// Table name for the TEntity which can be used for raw
    /// SQL queries.
    ///
    /// Name includes table plus schema prefix if the schema
    /// was provided in the model.
    /// 
    /// Example: Users or dbo.Users
    /// </summary>
    public string TableName
    {
        get
        {
            if (string.IsNullOrEmpty(_tableName))
            {
                var entityType = Context.Model.FindEntityType(typeof(TEntity));
                var tableName = entityType.GetTableName();
                var schema = entityType.GetDefaultSchema();
                    
                if (schema != null)
                    schema += ".";
                else
                    schema = "";

                _tableName = schema + tableName;
            }

            return _tableName;
        }
    }

}