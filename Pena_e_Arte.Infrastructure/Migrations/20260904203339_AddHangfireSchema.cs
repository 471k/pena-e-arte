using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <summary>
    /// Creates the Hangfire job-storage schema directly, instead of relying on
    /// Hangfire.MySqlStorage 2.0.3's own installer (now disabled via
    /// MySqlStorageOptions.PrepareSchemaIfNecessary = false).
    ///
    /// The library's own Install.sql (github.com/arnoldasgudas/Hangfire.MySqlStorage,
    /// tag 2.0.3, Hangfire.MySql/Install.sql) defines `DistributedLock` with NO primary
    /// key at all. DigitalOcean Managed MySQL enforces `sql_require_primary_key=ON` by
    /// default (most local/CI MySQL installs leave this off), which rejects that exact
    /// CREATE TABLE deterministically — every attempt died after creating exactly the 3
    /// tables that precede DistributedLock in the script (Job, Counter, AggregatedCounter),
    /// leaving the schema permanently 9 tables short. Invisible to any test suite whose
    /// MySQL container uses default settings. Found and fixed 2026-09-04 after it silently
    /// broke studio registration in production since launch (RegisterStudioHandler
    /// schedules a Hangfire job synchronously as part of the registration request).
    ///
    /// Every CREATE TABLE below is byte-identical to the library's own script (prefix
    /// substituted per MySqlStorageOptions.TablesPrefix = "hangfire_"), with one
    /// deliberate change: `hangfire_DistributedLock` gets a surrogate `Id AUTO_INCREMENT`
    /// primary key. Verified safe against the library's actual MySqlDistributedLock.cs
    /// before adding it — its INSERT/DELETE never reference Id, and Resource is
    /// deliberately non-unique (a stale/expired lock row must remain re-insertable after a
    /// crash that skipped Release(); a real PRIMARY KEY(Resource) would permanently
    /// deadlock that resource after any such crash).
    ///
    /// IF NOT EXISTS on every statement: safe to run against a database in any state,
    /// including one already fully or partially patched by the manual fix applied to
    /// staging/production on 2026-09-04 before this migration existed.
    /// </summary>
    public partial class AddHangfireSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_Job` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `StateId` int(11) DEFAULT NULL,
                  `StateName` nvarchar(20) DEFAULT NULL,
                  `InvocationData` longtext NOT NULL,
                  `Arguments` longtext NOT NULL,
                  `CreatedAt` datetime(6) NOT NULL,
                  `ExpireAt` datetime(6) DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  KEY `IX_hangfire_Job_StateName` (`StateName`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_Counter` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `Key` nvarchar(100) NOT NULL,
                  `Value` int(11) NOT NULL,
                  `ExpireAt` datetime DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  KEY `IX_hangfire_Counter_Key` (`Key`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_AggregatedCounter` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `Key` nvarchar(100) NOT NULL,
                  `Value` int(11) NOT NULL,
                  `ExpireAt` datetime DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  UNIQUE KEY `IX_hangfire_CounterAggregated_Key` (`Key`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            // The one deliberate deviation from the library's own script — see the class
            // summary above.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_DistributedLock` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `Resource` nvarchar(100) NOT NULL,
                  `CreatedAt` datetime(6) NOT NULL,
                  PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_Hash` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `Key` nvarchar(100) NOT NULL,
                  `Field` nvarchar(40) NOT NULL,
                  `Value` longtext,
                  `ExpireAt` datetime(6) DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  UNIQUE KEY `IX_hangfire_Hash_Key_Field` (`Key`,`Field`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_JobParameter` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `JobId` int(11) NOT NULL,
                  `Name` nvarchar(40) NOT NULL,
                  `Value` longtext,
                  PRIMARY KEY (`Id`),
                  CONSTRAINT `IX_hangfire_JobParameter_JobId_Name` UNIQUE (`JobId`,`Name`),
                  KEY `FK_hangfire_JobParameter_Job` (`JobId`),
                  CONSTRAINT `FK_hangfire_JobParameter_Job` FOREIGN KEY (`JobId`) REFERENCES `hangfire_Job` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_JobQueue` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `JobId` int(11) NOT NULL,
                  `FetchedAt` datetime(6) DEFAULT NULL,
                  `Queue` nvarchar(50) NOT NULL,
                  `FetchToken` nvarchar(36) DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  INDEX `IX_hangfire_JobQueue_QueueAndFetchedAt` (`Queue`,`FetchedAt`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_JobState` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `JobId` int(11) NOT NULL,
                  `CreatedAt` datetime(6) NOT NULL,
                  `Name` nvarchar(20) NOT NULL,
                  `Reason` nvarchar(100) DEFAULT NULL,
                  `Data` longtext,
                  PRIMARY KEY (`Id`),
                  KEY `FK_hangfire_JobState_Job` (`JobId`),
                  CONSTRAINT `FK_hangfire_JobState_Job` FOREIGN KEY (`JobId`) REFERENCES `hangfire_Job` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_Server` (
                  `Id` nvarchar(100) NOT NULL,
                  `Data` longtext NOT NULL,
                  `LastHeartbeat` datetime(6) DEFAULT NULL,
                  PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_Set` (
                  `Id` int(11) NOT NULL AUTO_INCREMENT,
                  `Key` nvarchar(100) NOT NULL,
                  `Value` nvarchar(256) NOT NULL,
                  `Score` float NOT NULL,
                  `ExpireAt` datetime DEFAULT NULL,
                  PRIMARY KEY (`Id`),
                  UNIQUE KEY `IX_hangfire_Set_Key_Value` (`Key`,`Value`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_State`
                (
                    `Id` int(11) NOT NULL AUTO_INCREMENT,
                    `JobId` int(11) NOT NULL,
                    `Name` nvarchar(20) NOT NULL,
                    `Reason` nvarchar(100) NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    `Data` longtext NULL,
                    PRIMARY KEY (`Id`),
                    KEY `FK_hangfire_HangFire_State_Job` (`JobId`),
                    CONSTRAINT `FK_hangfire_HangFire_State_Job` FOREIGN KEY (`JobId`) REFERENCES `hangfire_Job` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `hangfire_List`
                (
                    `Id` int(11) NOT NULL AUTO_INCREMENT,
                    `Key` nvarchar(100) NOT NULL,
                    `Value` longtext NULL,
                    `ExpireAt` datetime(6) NULL,
                    PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Children (FK to hangfire_Job) before the parent.
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_JobParameter`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_JobState`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_State`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_JobQueue`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_DistributedLock`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_Hash`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_Server`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_Set`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_List`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_Job`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_Counter`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `hangfire_AggregatedCounter`;");
        }
    }
}
