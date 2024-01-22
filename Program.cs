using System;
using System.Reflection.Emit;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Interop;
using System.Windows.Forms;
using System.IO;
using static System.Collections.Specialized.BitVector32;

class Program
{
    [STAThread]
    static void Main(string[] args) {
        Console.WriteLine("###################################################################################");
        Console.WriteLine("#                                                                                 #");
        Console.WriteLine("#            Veeam Hostname Update - Platima, 2024. https://plati.ma              #");
        Console.WriteLine("#                                                                                 #");
        Console.WriteLine("###################################################################################");
        Console.WriteLine("# This program is not endorsed by Veeam and you use it at your own risk.          #");
        Console.WriteLine("# Similarly, it may have bugs, and I offer no warranty nor take any liability.    #");
        Console.WriteLine("# The recommended method in case of a hostname or domain change is as-per KB4296. #");
        Console.WriteLine("# If you proceed with using this tool please ensure you have taken a backup first.#");
        Console.WriteLine("# Many exceptions are not handled, and if the program crashes it is recommended   #");
        Console.WriteLine("# that you remove the edb* files created alongside this program before retrying.  #");
        Console.WriteLine("#                                                                                 #");
        Console.WriteLine("# This tool must be run on the system where Veeam is installed to ensure that     #");
        Console.WriteLine("# the right ESE assemblies are used, else you may end up with version errors.     #");
        Console.WriteLine("###################################################################################\n");

        string? databasePath = args.Length > 0 ? args[0] : PromptForDatabasePath();
        if (string.IsNullOrEmpty(databasePath)) {
            Console.Error.WriteLine("Error: It appears no EDB file was chosen! Exiting.");
            Environment.Exit(1);
        }

        string? directoryPath = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrEmpty(directoryPath)) {
            Console.Error.WriteLine("Error: Could not extract parent folder from EDB location! Exiting.");
            Environment.Exit(1);
        }

        string proxiesTable = "Proxies";
        int pageSize = 8196;
        var dbId = new JET_DBID();

        Api.JetGetDatabaseFileInfo(databasePath, out pageSize, JET_DbInfo.PageSize);
        Api.JetSetSystemParameter(JET_INSTANCE.Nil, JET_SESID.Nil, JET_param.DatabasePageSize, pageSize, null);

        using (var instance = new Instance("instance")) {
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.TempPath, 0, directoryPath);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.LogFilePath, 0, directoryPath);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.SystemPath, 0, directoryPath);
            Api.JetSetSystemParameter(instance, JET_SESID.Nil, JET_param.BaseName, 0, "edb");

            instance.Init();

            using (var sesId = new Session(instance)) {
                try {
                    Api.JetAttachDatabase(sesId, databasePath, AttachDatabaseGrbit.None);
                } catch (EsentDatabaseDirtyShutdownException ex) {
                    Console.Error.WriteLine("#########");
                    Console.Error.WriteLine("# ERROR #");
                    Console.Error.WriteLine("#########\n");
                    Console.Error.WriteLine("The database was not shut down cleanly and may need recovery. Eg:");
                    Console.Error.WriteLine(" esentutl /r \"edb\"");
                    Console.Error.WriteLine("first to perform a recovery, and then optionally perform");
                    Console.Error.WriteLine($" esentutl /p \"{databasePath}\"");
                    Console.Error.WriteLine("to repair, however, again it is recommended to take a backup first!");

                    Console.Error.WriteLine($"\nEngine Error Message:\n {ex.Message}");

                    Console.Error.WriteLine("\n###########");
                    Console.Error.WriteLine("# Exiting #");
                    Console.Error.WriteLine("###########\n");
                }
                catch (EsentPrimaryIndexCorruptedException ex) {
                    Console.Error.WriteLine("#########");
                    Console.Error.WriteLine("# ERROR #");
                    Console.Error.WriteLine("#########\n");
                    Console.Error.WriteLine("The primary index is corrupt. This happens at times, just defrag. Eg:");
                    Console.Error.WriteLine($" esentutl /d \"{databasePath}\"");

                    Console.Error.WriteLine($"\nEngine Error Message:\n {ex.Message}");

                    Console.Error.WriteLine("\n###########");
                    Console.Error.WriteLine("# Exiting #");
                    Console.Error.WriteLine("###########\n");
                    Environment.Exit(1);
                    Environment.Exit(1);
                } catch (EsentException ex) {
                    Console.Error.WriteLine("#########");
                    Console.Error.WriteLine("# ERROR #");
                    Console.Error.WriteLine("#########\n");
                    Console.Error.WriteLine($"An ESE database error occurred:\n {ex.Message}\n");
                    Console.Error.WriteLine("###########");
                    Console.Error.WriteLine("# Exiting #");
                    Console.Error.WriteLine("###########\n");
                    Environment.Exit(1);
                }

                Api.JetOpenDatabase(sesId, databasePath, null, out dbId, OpenDatabaseGrbit.None);
                Console.WriteLine($"EDB File Opened: \"{databasePath}\"\n");

                using (var table = new Table(sesId, dbId, proxiesTable, OpenTableGrbit.None)) {
                    string[] columns = { "MachineName", "Host", "Identity" };
                    var columnids = new JET_COLUMNID[columns.Length];

                    for (int i = 0; i < columns.Length; i++) {
                        columnids[i] = Api.GetTableColumnid(sesId, table, columns[i]);
                    }

                    Api.JetSetTableSequential(sesId, table, SetTableSequentialGrbit.None);
                    Api.JetMove(sesId, table, JET_Move.First, MoveGrbit.None);

                    do {
                        Api.JetBeginTransaction(sesId);

                        for (int i = 0; i < columns.Length; i++) {
                            string oldValue = Api.RetrieveColumnAsString(sesId, table, columnids[i]);
                            Console.WriteLine($"Current {columns[i]}: {oldValue}");
                            Console.Write($"Enter new {columns[i]} (leave empty to keep current): ");
                            string? newValue = Console.ReadLine();

                            if (!string.IsNullOrEmpty(newValue)) {
                                byte[] newValueBytes = System.Text.Encoding.Unicode.GetBytes(newValue); // LongString, Unicode

                                Api.JetPrepareUpdate(sesId, table, JET_prep.Replace);
                                Api.SetColumn(sesId, table, columnids[i], newValueBytes);
                                Api.JetUpdate(sesId, table);
                            }

                            Console.WriteLine("");
                        }
                        Api.JetCommitTransaction(sesId, CommitTransactionGrbit.None);
                    }
                    while (Api.TryMoveNext(sesId, table));
                }
                
                Api.JetCloseDatabase(sesId, dbId, CloseDatabaseGrbit.None);
                Api.JetDetachDatabase(sesId, databasePath);
                Console.WriteLine("Operation appears to have completed successfully!\nHit [enter] to close!");
                Console.ReadLine();
            }
        }
    }

    static string? PromptForDatabasePath()  {
        using(OpenFileDialog openFileDialog = new OpenFileDialog()) {
            openFileDialog.InitialDirectory = "C:\\ProgramData\\Veeam\\Backup365";
            openFileDialog.Filter = "EDB files (*.edb)|*.edb|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            Console.WriteLine("Please choose your ConfigDb\\config.edb file to work on.");

            if (openFileDialog.ShowDialog() == DialogResult.OK) {
                return openFileDialog.FileName;
            }
        }
        return null;
    }
}
