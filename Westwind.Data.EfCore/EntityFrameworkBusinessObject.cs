using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Westwind.Utilities;
using Westwind.Utilities.Data;

namespace Westwind.Data.EfCore
{

    /// <summary>
    /// A business object that acts as a thin layer over Entity Framework
    /// CRUD operations.
    ///
    /// Provides for:
    ///
    /// * Simplified CRUD operations
    /// * Error Handling
    /// * Validation
    /// 
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TEntity"></typeparam>    
    public class EntityFrameworkBusinessObject<TContext, TEntity>  
        : IDisposable
        where TContext : DbContext
        where TEntity : class, new()
    {      
        public EntityFrameworkBusinessObject(TContext context)
        {
            Context = context;            
            DatabaseSettings = new BusinessObjectDatabaseSettings<TEntity>(Context);            
        }

        /// <summary>
        /// Entity instance set by Load and Create and
        /// Load operations. 
        /// </summary>
        public TEntity Entity { get; set; }

        /// <summary>
        /// Instance of the DbContext. Must be passed or 
        /// injected.
        /// </summary>
        public TContext Context { get; set; }

        
        /// <summary>
        /// Raw Sql Data Access Component to run additional 
        /// SQL commands and return results via ADO.NET
        /// </summary>
        public SqlDataAccess Db
        {
            get
            {
                if (_db == null)
                    _db = new SqlDataAccess(DatabaseSettings.ConnectionString);

                return _db;
            }
            set { _db = value; }
        }
        private SqlDataAccess _db;


        /// <summary>
        /// Database related settings like ConnectionString, Entity Table name
        /// and an instance of the activated instance of the DbSet.
        /// </summary>
        public BusinessObjectDatabaseSettings<TEntity> DatabaseSettings { get; }


        /// <summary>
        /// Configuration options for the business object
        /// </summary>
        public BusinessObjectOptions Options { get; } = new BusinessObjectOptions();       


        /// <summary>
        /// A collection that can be used to hold errors or
        /// validation errors. 
        /// 
        /// Note you have to explicitly call Validate() to 
        /// validate explicit business rules you define in code
        /// as well as entity validation rules.
        /// 
        /// Calling Save() will not call this method to validate
        /// as it can potentially operate on multiple entities,
        /// but it will fail on entity validations.
        /// </summary>        
        public ValidationErrorCollection ValidationErrors
        {
            get
            {                
                if (_validationErrors == null)
                    _validationErrors = new ValidationErrorCollection();
                return _validationErrors;
            }
        }
        ValidationErrorCollection _validationErrors;

        /// <summary>
        /// Determines whether there are any validation errors
        /// </summary>
        public bool HasValidationErrors => ValidationErrors.Count > 0;
        
        /// <summary>
        /// Error Message of the last exception
        /// </summary>
        public string ErrorMessage
        {
            get
            {
                if (ErrorException == null)
                    return "";
                return ErrorException.Message;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    ErrorException = null;
                else
                    // Assign a new exception
                    ErrorException = new Exception(value);
            }
        }

        /// <summary>
        /// Instance of an exception object that caused the last error
        /// </summary>
        public Exception ErrorException { get; set; }



        #region Create and Attach
        /// <summary>
        /// Creates a new instance of the entity type 
        /// associated to this Repo
        /// </summary>
        /// <returns></returns>
        public virtual TEntity Create()
        {
            Entity = new TEntity();

            Context.Add<TEntity>(Entity);
            OnAfterCreated(Entity);

            return Entity;
        }

        /// <summary>
        /// Attaches an existing entity to the context.
        /// </summary>
        /// <param name="entity"></param>
        /// <exception cref="ArgumentException">Thrown on null parameter passed</exception>
        /// <returns></returns>
        public virtual TEntity Create(TEntity entity)
        {
            if (entity is null)
                throw new ArgumentException("Can't pass a null reference to Create().");

            Entity = entity;
            Context.Add<TEntity>(Entity);
            OnAfterCreated(Entity);

            return Entity;
        }
        /// <summary>
        /// Creates an instance of an enity type different
        /// than the one associated with this repo. Specify
        /// the entity type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public virtual T Create<T>()
          where T : class, new()
        {
            var entity = new T();
            Context.Add<T>(entity);
            return entity;
        }


        /// <summary>
        /// Attaches an untracked to the internal context and 
        /// marks it as modified optionally
        /// Note: child elements need to be manually added.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public TEntity Attach(TEntity entity, bool addNew = false)
        {
            if (addNew)
                Context.Add<TEntity>(entity);
            else
            {
                var entry = Context.Attach<TEntity>(entity);
                entry.State = EntityState.Modified;
            }

            Entity = entity;

            return Entity;
        }

        public void GetEntityState(TEntity entity)
        {

        }
        #endregion

        #region Load Async
        /// <summary>
        /// Loads an entity by id. Default implementation returns only
        /// the base entity without relationships loaded.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual async Task<TEntity> LoadAsync(object id)
        {            
            return await LoadBaseAsync(id);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual async Task<T> LoadAsync<T>(object id)
            where T : class, new()
        {
            return await LoadBaseAsync<T>(id);
        }


        /// <summary>
        /// Loads an instance based on its key field id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual async Task<TEntity> LoadBaseAsync(object id)
        {
            object match = null;
            try
            {
                match = Context.Find<TEntity>(id);
                if (match == null)
                {
                    Entity = null;
                    SetError("Unable to find matching entity for key");
                    return await Task.FromResult<TEntity>(null);
                }
            }
            catch (Exception ex)
            {
                // handles Sql errors                                
                SetError(ex);
            }
            
            // Assign to internal member
            Entity = match as TEntity;

            OnAfterLoaded(Entity);
                        
            return await Task.FromResult(Entity);            
        }

        /// <summary>
        /// Loads an entity instance based on its key field id
        /// and a custom type. OnEntityLoaded() is not fired in
        /// in this scenario as the type doesn't match.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual async Task<T> LoadBaseAsync<T>(object id)
            where T : class, new()
        {            
            T entity = null;
            try
            {
                var set = Context.Set<T>();                
                entity = set.Find(id);
                if (entity == null)
                {
                    SetError("Unable to find matching entity for key");
                    return await Task.FromResult<T>(null);
                }
            }
            catch (Exception ex)
            {
                // handles Sql errors                                
                SetError(ex);
            }
            
            return await Task.FromResult<T>(entity);            
        }




        /// <summary>
        /// Loads an entity by an expression
        /// </summary>
        /// <param name="whereClauseLambda"></param>
        /// <returns></returns>
        protected virtual async Task<TEntity> LoadBaseAsync(Expression<Func<TEntity, bool>> whereClauseLambda)
        {
            SetError();

            try
            {                
                Entity = await DatabaseSettings.DbSet.FirstOrDefaultAsync(whereClauseLambda);
                if (Entity == null)
                {
                    SetError("Unable to load entity");
                    return null;
                }
                OnAfterLoaded(Entity);
            }
            catch (InvalidOperationException)
            {
                Entity = null;

                // Handles errors where an invalid Id was passed, but SQL is valid                
                SetError("Couldn't load entity...");                
            }
            catch (Exception ex)
            {
                Entity = null;
                // handles Sql errors                                
                SetError(ex);
            }

            return Entity;
        }

        /// <summary>
        /// Loads an entity by an expression
        /// </summary>
        /// <param name="whereClauseLambda"></param>
        /// <returns></returns>
        protected virtual TEntity LoadBase(Expression<Func<TEntity, bool>> whereClauseLambda)
        {
            SetError();

            try
            {                
                Entity = DatabaseSettings.DbSet.FirstOrDefault(whereClauseLambda);          
                
                if (Entity == null)
                {
                    SetError("Unable to load entity");
                    return null;
                }

                OnAfterLoaded(Entity);
            }
            catch (InvalidOperationException)
            {
                Entity = null;

                // Handles errors where an invalid Id was passed, but SQL is valid                
                SetError("Couldn't load entity...");                
            }
            catch (Exception ex)
            {
                Entity = null;
                // handles Sql errors                                
                SetError(ex);
            }

            return Entity;
        }
        #endregion

        #region load Sync

        /// <summary>
        /// Loads an entity by id. Default implementation returns only
        /// the base entity without relationships loaded.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual TEntity Load(object id)
        {
            return LoadBase(id);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual T Load<T>(object id)
            where T : class, new()
        {
            return LoadBase<T>(id);
        }

        /// <summary>
        /// Loads an instance based on its key field id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual TEntity LoadBase(object id)
        {
            object match = null;
            try
            {
                match = Context.Find<TEntity>(id);
                if (match == null)
                {
                    Entity = null;
                    SetError("Unable to find matching entity for key");
                    return null;
                }
            }
            catch (Exception ex)
            {
                // handles Sql errors                                
                SetError(ex);
            }

            // Assign to internal member
            Entity = match as TEntity;

            OnAfterLoaded(Entity);

            return Entity;
        }

        /// <summary>
        /// Loads an entity instance based on its key field id
        /// and a custom type. OnEntityLoaded() is not fired in
        /// in this scenario as the type doesn't match.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual T LoadBase<T>(object id)
            where T : class, new()
        {
            T entity = null;
            try
            {
                var set = Context.Set<T>();
                entity = set.Find(id);
                if (entity == null)
                {
                    SetError("Unable to find matching entity for key");
                    return null;
                }
            }
            catch (Exception ex)
            {
                // handles Sql errors                                
                SetError(ex);
            }

            return entity;
        }
        /// <summary>
        /// Loads an entity by an expression
        /// </summary>
        /// <param name="whereClauseLambda"></param>
        /// <returns></returns>
        protected virtual TEntity LoadBaseSync(Expression<Func<TEntity, bool>> whereClauseLambda)
        {
            SetError();

            try
            {
                Entity = DatabaseSettings.DbSet.FirstOrDefault(whereClauseLambda);
            }
            catch (InvalidOperationException)
            {
                Entity = null;

                // Handles errors where an invalid Id was passed, but SQL is valid                
                SetError("Couldn't load entity...");
            }
            catch (Exception ex)
            {
                Entity = null;
                // handles Sql errors                                
                SetError(ex);
            }

            return Entity;
        }
        #endregion



        #region Saving and Deleting
        /// <summary>
        /// Saves changes to the repo
        /// </summary>
        /// <param name="entity">
        /// Pass the entity to save
        /// 
        /// You can omit the parameter if you just want to save the 
        /// current context. When no entity is passed no validation
        /// or OnBeforeSave()/OnAfterSave() are applied - just a plain
        /// SaveChanges        
        /// </param>
        /// <param name="useTransaction">
        /// not implemented yet
        /// </param>
        public virtual async Task<bool> SaveAsync(TEntity entity = null)
        {
            if (entity == null)
                entity = Entity;

            if (entity != null)
            {
                if (!OnBeforeSave(entity))
                    return false;

                if (Options.AutoValidate && !Validate(entity))
                    return false;

                var entry = Context.Entry(entity);
                if (entry.State == EntityState.Detached)
                {
                    Context.Attach(entity);

                    // see if it exists
                    TEntity match = null;
                    try
                    {
                        object id = Context.GetEntityKey(entity).FirstOrDefault();
                        if (id != null)
                            match = DatabaseSettings.DbSet.Find(id);
                    }
                    catch
                    {
                    }

                    if (match != null)
                        entry.State = EntityState.Modified;
                    else
                        entry.State = EntityState.Added;
                }
            }

            int result = -1;
            try
            {
                result = await Context.SaveChangesAsync();
                if (result == -1)
                    return false;
            }
            catch (Exception ex)
            {
                if (Options.ThrowExceptions)
                    throw;

                SetError(ex.GetBaseException());
                return false;
            }

            if (result == -1)
                return false;

            if (entity != null && !OnAfterSave(entity))
                return false;

            return true;
        }

        /// <summary>
        /// A transacted version of SaveAsync that explicitly uses a transaction around the
        /// save operation to handle OnBeforeSave() and OnAfterSave() operations.        
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="useTransaction"></param>
        /// <returns></returns>
        public virtual async Task<bool> SaveAsync(TEntity entity, bool useTransaction)
        {
            if (useTransaction)
            {
                using (var tx = Context.Database.BeginTransaction())
                {
                    if (await SaveAsync(entity))
                    {
                        tx.Commit();
                        return true;
                    }
                    return false;
                }
            }

            return await SaveAsync(entity);
        }

        /// <summary>
        /// Saves the underlying data with Before and After Save hooks
        /// and optional validation.
        /// 
        /// If entity is not passed on SaveChanges is called
        /// </summary>
        /// <param name="entity">Enity that is saved and updated.</param>
        /// <remarks>
        /// For raw saving without pre-/post processing use SaveChanges()
        /// </remarks>
        /// <returns></returns>
        public virtual bool Save(TEntity entity = null)
        {
            if (entity == null)
                entity = Entity;

            if (entity != null)
            {
                if (!OnBeforeSave(entity))
                    return false;

                if (Options.AutoValidate && !Validate(entity))
                    return false;

                var entry = Context.Entry(entity);
                if (entry.State == EntityState.Detached)
                {
                    Context.Attach(entity);
                    if (Context.GetEntityKey(entity).Any())
                        entry.State = EntityState.Modified;
                    else
                        entry.State = EntityState.Added;
                }
            }

            try
            {
                int result = Context.SaveChanges();
                if (result == -1)
                    return false;
            }
            catch (Exception ex)
            {
                if (Options.ThrowExceptions)
                    throw;

                SetError(ex.GetBaseException());
                return false;
            }

            if (entity != null) { 
                if (!OnAfterSave(entity))
                    return false;
            }

            return true;
        }


        /// <summary>
        /// Thin Wrapper around the Context.SaveChanges that doesn't throw.
        /// Use this for multiple data operations that are not explicitly
        /// saving single instances and bypass validation and pre/post
        /// processing.
        ///
        /// In most cases Save() is more adequate.
        /// </summary>
        /// <remarks>
        /// For validation and pre/post-processing hooks use the Save method.
        /// </remarks>
        /// <returns>number of records affected. -1 for failure.</returns>
        public int SaveChanges()
        {
            int result = -1;
            try
            {
                result = Context.SaveChanges();
                if (result == -1)
                    return -1;
            }
            catch (Exception ex)
            {
                if (Options.ThrowExceptions)
                    throw;

                SetError(ex.GetBaseException());
                return -1;
            }

            return result;
        }

        /// <summary>
        /// Thin Wrapper around the Context.SaveChanges that doesn't throw.
        /// Use this for multiple data operations that are not explicitly
        /// saving single instances and bypass validation and pre/post
        /// processing.
        ///
        /// In most cases SaveAsync() is more adequate as it handles
        /// individual entity CRUD and fixups.
        /// </summary>
        /// <remarks>
        /// For validation and pre/post-processing hooks use the Save method.
        /// </remarks>
        /// <returns>number of records affected. -1 for failure.</returns>
        public async Task<int> SaveChangesAsync()
        {
            int result = -1;
            try
            {
                result = await Context.SaveChangesAsync();
                if (result == -1)
                    return -1;
            }
            catch (Exception ex)
            {
                if (Options.ThrowExceptions)
                    throw;

                SetError(ex.GetBaseException());
                return -1;
            }

            return result;
        }


        /// <summary>
        /// Deletes an entity from the main entity set
        /// based on a key value.
        ///
        /// By default doesn't execute the SQL command but just
        /// updates the context unless `saveChanges` is set to true.
        /// </summary>
        /// <param name="id">Id of the key to delete</param>
        /// <param name="saveChanges">if true changes are saved to disk. Otherwise entity is removed from context only</param>
        /// <returns></returns>
        public virtual bool Delete(object id, bool saveChanges = false, bool useTransaction = false)
        {
            TEntity entity = DatabaseSettings.DbSet.Find(id);
            return Delete(entity, saveChanges: saveChanges, useTransaction: useTransaction);
        }

        /// <summary>
        /// removes an individual entity instance.
        /// 
        /// This method allows specifying an entity in a dbSet other
        /// then the main one as long as it's specified by the dbSet
        /// parameter.
        ///
        /// By default doesn't execute the SQL command but just
        /// updates the context unless `saveChanges` is set to true.
        /// </summary>
        /// <param name="entity">The entity to delete</param>        
        /// Allows specifying the DbSet to which the entity passed belongs.
        /// If not specified the current DbSet for the current entity is used </param>
        /// <param name="saveChanges">Optional - 
        /// If true does a Context.SaveChanges. Set to false
        /// when other changes in the Context are pending and you don't want them to commit
        /// immediately
        /// </param>
        /// <param name="useTransaction">Optional - 
        /// If true the Delete operation is wrapped into a TransactionScope transaction that
        /// ensures that OnBeforeDelete and OnAfterDelete all fire within the same Transaction scope.
        /// Defaults to false as to improve performance.
        /// </param>
        public virtual bool Delete(TEntity entity, bool saveChanges = true, bool useTransaction = false)
        {            
            if (entity == null)
                return true;            

             if (!DeleteInternal(entity, saveChanges))
                return false;
          
            return true;
        }


        /// <summary>
        /// This is a raw delete operation that uses a raw SQL command
        /// to delete an object directly bypassing the EF DbContext.
        /// </summary>
        /// <remarks>
        /// Bypasses DbContext.
        /// 
        /// Returns true even if no records were deleted as it it means
        /// that the records are in fact not existing any longer.
        /// False and errors occur only if the SQL command execution fails.
        /// </remarks>
        /// <param name="id"></param>
        public virtual bool DeleteDirect(object id)
        {               
            string sql = $"delete from [{DatabaseSettings.TableName}] where Id=@0";

            var result = Db.ExecuteNonQuery(sql, id);
            if (result > -1)           
                return true;
            
            if (Db.ErrorException != null && Options.ThrowExceptions)
                throw Db.ErrorException;                
            
            SetError(Db.ErrorMessage);
            return false;
        }


        /// <summary>
        /// Raw delete operation with a filter string that is used as
        /// a raw `WHERE` clause in a direct SQL statement.
        /// </summary>
        /// <param name="sqlFilter">SQL Where clause as a string</param>
        /// <returns></returns>
        public virtual int DeleteWhereDirect(string sqlFilter = null)
        {
            string sql = "delete from [{Database.TableName}])";
            if (!string.IsNullOrEmpty(sqlFilter))
            {

                sqlFilter = sqlFilter.Replace("'", "''");
                sql = "delete from [{Database.TableName}]) where '{sqlFilter}'";
            }

            int result = Db.ExecuteNonQuery(sql);

            if (result > -1)
                return result;

            if (Db.ErrorException != null && Options.ThrowExceptions)
                throw Db.ErrorException;

            SetError(Db.ErrorMessage);

            return result;
        }

        /// <summary>
        /// Actual delete operation that removes an entity but that
        /// can be overridden for custom behavior if necessary.
        /// </summary>
        protected virtual bool DeleteInternal(TEntity entity, bool saveChanges = false, bool useTransaction = false)
        {
            if (!OnBeforeDelete(entity))
                return false;

            var dbSet = Context.Set<TEntity>();

            try
            {
                dbSet.Remove(entity);

                // one operation that immediately submits
                if (saveChanges)
                    Context.SaveChanges();

                if (!OnAfterDelete(entity))
                    return false;
            }
            catch (Exception ex)
            {
                SetError(ex, true);
                return false;
            }
            
            return true;
        }
        #endregion


        #region Helpers

        /// <summary>
        /// Determines whether the entity being added is a new entry.
        /// 
        /// The entity should belong to a context, if it doesn't 
        /// null is returned.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public bool? IsNewEntity(TEntity entity)
        {
            var entry = Context.Entry(entity);
            if (entry == null || entry.State == EntityState.Detached)
                return null;

            return entry.State == EntityState.Added;
        }

        public bool? IsNewEntity<T>(T entity)
            where  T :  class
        {
            var entry = Context.Entry(entity);
            if (entry == null || entry.State == EntityState.Detached)
                return null;

            return entry.State == EntityState.Added;
        }
        #endregion

        #region validation

        /// <summary>
        /// Override this method to validate your business object.
        /// Set Validation Errors and return true or false from
        /// this method to indicate success or failure.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public virtual bool Validate(TEntity entity, bool dontClearValidationErrors = false)
        {
            if (!dontClearValidationErrors)
                ValidationErrors.Clear();

            bool isValid = OnValidate(entity);
            if (!isValid)
                SetError(ValidationErrors.ToString());

            return isValid;
        }

        /// <summary>
        /// Override this method to handle entity validation. Add any validation
        /// errors to the ValidationErrors collection to indicate that validation
        /// should fail.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <returns></returns>
        protected virtual bool OnValidate(TEntity entity)
        {
            // *** typical use case
            // if (validationRuleFailed)
            //    ValidationErrors.Add("Error Message","object id");
            // return ValidationErrors.Count < 1;  // true - validate succeeds

            return ValidationErrors.Count < 1;
        }

        #endregion


        #region event hooks
        
        /// <summary>
        /// Fired after an entity was created
        /// </summary>
        /// <param name="entity"></param>
        protected virtual void OnAfterCreated(TEntity entity)
        {

        }

        /// <summary>
        /// Fired after an entity was loaded
        /// </summary>
        /// <param name="entity"></param>
        protected virtual void OnAfterLoaded(TEntity entity)
        {
        }

        /// <summary>
        /// Overridable before save hook.
        /// Return false to indicate that the 
        /// Save() operation should not occur
        /// </summary>
        /// <returns></returns>
        protected virtual bool OnBeforeSave(TEntity entity)
        {
            return true;
        }

        /// <summary>
        /// Overridable after save hook. Called
        /// after SaveChanges() has completed.
        /// Return false in order to indicate
        /// to the caller that the save operation
        /// did not complete successfull (but
        /// data has been save
        /// </summary>
        protected virtual bool OnAfterSave(TEntity entity)
        {
            return true;
        }

        /// <summary>
        /// Overridable hook that is called after an entity is deleted.
        /// Return false return false for the Delete() method.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        protected virtual bool OnAfterDelete(TEntity entity)
        {
            return true;
        }
        /// <summary>
        /// Overridable hook that is called before an entity is deleted.
        /// Return false to abort delete operation.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        protected virtual bool OnBeforeDelete(TEntity entity)
        {
            return true;
        }

        #endregion

        #region Error Handling

        /// <summary>
        /// Sets an internal error message.
        /// </summary>
        /// <param name="Message"></param>
        public void SetError(string Message)
        {
            if (string.IsNullOrEmpty(Message))
            {
                ErrorException = null;
                return;
            }

            ErrorException = new ApplicationException(Message);

            //if (Options.ThrowExceptions)
            //    throw ErrorException;
        }

        /// <summary>
        /// Sets an internal error exception
        /// </summary>
        /// <param name="ex"></param>
        public void SetError(Exception ex, bool checkInnerException = false)
        {
            ErrorException = ex;

            if (checkInnerException)
            {
                while (ErrorException.InnerException != null)
                {
                    ErrorException = ErrorException.InnerException;
                }
            }

            ErrorMessage = ErrorException.Message;
            if (ex != null && Options.ThrowExceptions)
                throw ex;
        }

        /// <summary>
        /// Clear out errors
        /// </summary>
        public void SetError()
        {
            ErrorException = null;
            ErrorMessage = null;
        }

        public void Dispose()
        {
            Context?.Dispose();
            Context = null;
        }
        #endregion


    }

    public class BusinessObjectOptions
    {
        /// <summary>
        /// Determines whether the Validate method is automatically called
        /// in the Save() operation
        /// </summary>
        public bool AutoValidate { get; set; }

        /// <summary>
        /// Determines whether Save operations throw 
        /// exceptions or return errors as messages
        /// </summary>
        public bool ThrowExceptions { get; set; }

    }
}