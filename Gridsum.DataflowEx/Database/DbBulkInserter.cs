﻿using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Gridsum.DataflowEx.Database
{
    /// <summary>
    /// The class helps you to bulk insert parsed objects to the database. 
    /// </summary>
    /// <typeparam name="T">The db-mapped type of parsed objects (usually generated by EF/linq2sql)</typeparam>
    public class DbBulkInserter<T> : BlockContainer<T> where T:class
    {
        private readonly int m_bulkSize;
        private readonly string m_dbBulkInserterName;
        private readonly BatchBlock<T> m_batchBlock;
        private readonly ActionBlock<T[]> m_actionBlock;

        public DbBulkInserter(string connectionString, string destTable, BlockContainerOptions options, string destLabel, int bulkSize = 4096 * 2, string dbBulkInserterName = null) 
            : base(options)
        {
            m_bulkSize = bulkSize;
            m_dbBulkInserterName = dbBulkInserterName;
            m_batchBlock = new BatchBlock<T>(bulkSize);
            m_actionBlock = new ActionBlock<T[]>(async array =>
            {
                LogHelper.Logger.Debug(h => h("{3} starts bulk-inserting {0} {1} to db table {2}", array.Length, typeof(T).Name, destTable, this.Name));
                await DumpToDB(array, destTable, connectionString, destLabel);
                LogHelper.Logger.Info(h => h("{3} bulk-inserted {0} {1} to db table {2}", array.Length, typeof(T).Name, destTable, this.Name));
            });
            m_batchBlock.LinkTo(m_actionBlock, m_defaultLinkOption);

            RegisterChild(m_batchBlock);
            RegisterChild(m_actionBlock);
        }

        private async Task DumpToDB(IEnumerable<T> data, string destTable, string connectionString, string destLabel)
        {
            using (var bulkReader = new BulkDataReader<T>(TypeAccessorManager<T>.GetAccessorByDestLabel(destLabel, connectionString, destTable), data))
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    using (var bulkCopy = new SqlBulkCopy(conn))
                    {
                        foreach (SqlBulkCopyColumnMapping map in bulkReader.ColumnMappings)
                        {
                            bulkCopy.ColumnMappings.Add(map);
                        }

                        bulkCopy.DestinationTableName = destTable;
                        bulkCopy.BulkCopyTimeout = 0;
                        bulkCopy.BatchSize = m_bulkSize;

                        // Write from the source to the destination.
                        await bulkCopy.WriteToServerAsync(bulkReader);
                    }
                }
            }
        }

        public override ITargetBlock<T> InputBlock { get { return m_batchBlock; } }
        
        public override string Name
        {
            get {
                return m_dbBulkInserterName ?? base.Name;
            }
        }
    }
}
