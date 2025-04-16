------------------------------------------------                        --------------------------------------------------------------
------------------------------------------------  DROP EXISTING TABLES  --------------------------------------------------------------
------------------------------------------------                        --------------------------------------------------------------

-- Drop foreign key constraints if they exist
ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_Nodes_ContentListId`;
ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_Nodes_CreatedById`;
ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_Nodes_ModifiedById`;

ALTER TABLE `BinaryProperties` DROP FOREIGN KEY IF EXISTS `FK_BinaryProperties_PropertyTypes`;
ALTER TABLE `BinaryProperties` DROP FOREIGN KEY IF EXISTS `FK_BinaryProperties_Versions`;
ALTER TABLE `BinaryProperties` DROP FOREIGN KEY IF EXISTS `FK_BinaryProperties_Files`;

ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_LockedBy`;
ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_Parent`;
ALTER TABLE `Nodes` DROP FOREIGN KEY IF EXISTS `FK_Nodes_NodeTypes`;

ALTER TABLE `ReferenceProperties` DROP FOREIGN KEY IF EXISTS `FK_ReferenceProperties_PropertyTypes`;
ALTER TABLE `NodeTypes` DROP FOREIGN KEY IF EXISTS `FK_NodeTypes_NodeTypes`;
ALTER TABLE `LongTextProperties` DROP FOREIGN KEY IF EXISTS `FK_LongTextProperties_PropertyTypes`;
ALTER TABLE `LongTextProperties` DROP FOREIGN KEY IF EXISTS `FK_LongTextProperties_Versions`;

ALTER TABLE `Versions` DROP FOREIGN KEY IF EXISTS `FK_Versions_Nodes`;
ALTER TABLE `Versions` DROP FOREIGN KEY IF EXISTS `FK_Versions_Nodes_CreatedBy`;
ALTER TABLE `Versions` DROP FOREIGN KEY IF EXISTS `FK_Versions_Nodes_ModifiedBy`;

DROP VIEW IF EXISTS `NodeInfoView`;
DROP VIEW IF EXISTS `PermissionInfoView`;
DROP VIEW IF EXISTS `ReferencesInfoView`;
DROP VIEW IF EXISTS `MembershipInfoView`;

DROP TABLE IF EXISTS `PropertyTypes`;
DROP TABLE IF EXISTS `NodeTypes`;
DROP TABLE IF EXISTS `ContentListTypes`;
DROP TABLE IF EXISTS `ReferenceProperties`;
DROP TABLE IF EXISTS `BinaryProperties`;
DROP TABLE IF EXISTS `Files`;
DROP TABLE IF EXISTS `LongTextProperties`;
DROP TABLE IF EXISTS `Nodes`;
DROP TABLE IF EXISTS `Versions`;
DROP TABLE IF EXISTS `JournalItems`;

DROP TABLE IF EXISTS `LogEntries`;
DROP TABLE IF EXISTS `IndexingActivities`;
DROP TABLE IF EXISTS `WorkflowNotification`;
DROP TABLE IF EXISTS `SchemaModification`;
DROP TABLE IF EXISTS `Packages`;
DROP TABLE IF EXISTS `TreeLocks`;
DROP TABLE IF EXISTS `AccessTokens`;
DROP TABLE IF EXISTS `SharedLocks`;

------------------------------------------------                           --------------------------------------------------
------------------------------------------------ ENABLE SNAPSHOT ISOLATION --------------------------------------------------
------------------------------------------------                           --------------------------------------------------

-- MySQL does not have a direct equivalent for setting single-user mode or enabling snapshot isolation as in SQL Server.
-- The following script closes all connections to the database and sets up the database for modifications.

-- Close all connections to the database
SET @dbName = DATABASE();

-- Terminate all existing connections to the database
SET @cmd0 = CONCAT('SELECT CONCAT("KILL ", id, ";") AS kill_query FROM INFORMATION_SCHEMA.PROCESSLIST WHERE DB = "', @dbName, '"');
PREPARE stmt0 FROM @cmd0;
EXECUTE stmt0;
DEALLOCATE PREPARE stmt0;

-- MySQL does not support snapshot isolation, so no equivalent commands are required for snapshot isolation.
-- Transactions in MySQL use InnoDB engine, which supports Repeatable Read isolation level by default.

-- You may enable read committed isolation level globally or for the session if required.
-- Uncomment the next line if read committed isolation level is necessary for your application.
-- SET GLOBAL transaction_isolation = 'READ-COMMITTED';
-- SET SESSION transaction_isolation = 'READ-COMMITTED';

------------------------------------------------               --------------------------------------------------------------
------------------------------------------------ CREATE TABLES --------------------------------------------------------------
------------------------------------------------               --------------------------------------------------------------

-- Create the ReferenceProperties table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateReferencePropertiesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME 
        FROM INFORMATION_SCHEMA.TABLES 
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ReferenceProperties'
    ) THEN
        CREATE TABLE `ReferenceProperties` (
            `ReferencePropertyId` INT NOT NULL AUTO_INCREMENT,
            `VersionId` INT NOT NULL,
            `PropertyTypeId` INT NOT NULL,
            `ReferredNodeId` INT NOT NULL,
            PRIMARY KEY (`ReferencePropertyId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
    
    -- Create the first index if it does not exist
    IF NOT EXISTS (
        SELECT INDEX_NAME 
        FROM INFORMATION_SCHEMA.STATISTICS 
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ReferenceProperties' AND INDEX_NAME = 'IX_VersionIdPropertyTypeId'
    ) THEN
        CREATE INDEX `IX_VersionIdPropertyTypeId` ON `ReferenceProperties` (`VersionId`, `PropertyTypeId`);
    END IF;

    -- Create the second index if it does not exist
    IF NOT EXISTS (
        SELECT INDEX_NAME 
        FROM INFORMATION_SCHEMA.STATISTICS 
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ReferenceProperties' AND INDEX_NAME = 'IX_ReferenceProperties_ReferredNodeId'
    ) THEN
        CREATE INDEX `IX_ReferenceProperties_ReferredNodeId` ON `ReferenceProperties` (`ReferredNodeId`);
    END IF;
END $$

DELIMITER ;

-- Create the LongTextProperties table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateLongTextPropertiesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'LongTextProperties'
    ) THEN
        CREATE TABLE `LongTextProperties` (
            `LongTextPropertyId` INT NOT NULL AUTO_INCREMENT,
            `VersionId` INT NOT NULL,
            `PropertyTypeId` INT NOT NULL,
            `Length` INT NULL,
            `Value` TEXT NULL,
            PRIMARY KEY (`LongTextPropertyId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

-- Create the BinaryProperties table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateBinaryPropertiesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'BinaryProperties'
    ) THEN
        CREATE TABLE `BinaryProperties` (
            `BinaryPropertyId` INT NOT NULL AUTO_INCREMENT,
            `VersionId` INT NULL,
            `PropertyTypeId` INT NULL,
            `FileId` INT NOT NULL,
            PRIMARY KEY (`BinaryPropertyId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

-- Create the Files table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateFilesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Files'
    ) THEN
        CREATE TABLE `Files` (
            `FileId` INT NOT NULL AUTO_INCREMENT,
            `ContentType` NVARCHAR(450) NOT NULL,
            `FileNameWithoutExtension` NVARCHAR(450) NULL,
            `Extension` NVARCHAR(50) NOT NULL,
            `Size` BIGINT NOT NULL,
            `Checksum` VARCHAR(200) NULL,
            `Stream` LONGBLOB NULL,
            `CreationDate` DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
            `RowGuid` CHAR(36) NOT NULL UNIQUE DEFAULT (UUID()),
            `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            `Staging` BOOLEAN NULL,
            `StagingVersionId` INT NULL,
            `StagingPropertyTypeId` INT NULL,
            `IsDeleted` BOOLEAN NULL,
            `BlobProvider` NVARCHAR(450) NULL,
            `BlobProviderData` LONGTEXT NULL,
            PRIMARY KEY (`FileId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

-- Create the Nodes table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateNodesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Nodes'
    ) THEN
        CREATE TABLE `Nodes` (
            `NodeId` INT NOT NULL AUTO_INCREMENT,
            `NodeTypeId` INT NOT NULL,
            `ContentListTypeId` INT NULL,
            `ContentListId` INT NULL,
            `CreatingInProgress` TINYINT NOT NULL DEFAULT 0,
            `IsDeleted` TINYINT NOT NULL,
            `IsInherited` TINYINT NOT NULL DEFAULT 1,
            `ParentNodeId` INT NULL,
            `Name` NVARCHAR(450) NOT NULL,
            `Path` NVARCHAR(450) CHARACTER SET latin1 COLLATE latin1_general_ci NOT NULL,
            `Index` INT NOT NULL,
            `Locked` TINYINT NOT NULL,
            `LockedById` INT NULL,
            `ETag` VARCHAR(50) NOT NULL,
            `LockType` INT NOT NULL,
            `LockTimeout` INT NOT NULL,
            `LockDate` DATETIME(6) NOT NULL,
            `LockToken` VARCHAR(50) NOT NULL,
            `LastLockUpdate` DATETIME(6) NOT NULL,
            `LastMinorVersionId` INT NULL,
            `LastMajorVersionId` INT NULL,
            `CreationDate` DATETIME(6) NOT NULL,
            `CreatedById` INT NOT NULL,
            `ModificationDate` DATETIME(6) NOT NULL,
            `ModifiedById` INT NOT NULL,
            `DisplayName` NVARCHAR(450) NULL,
            `IsSystem` TINYINT NULL,
            `OwnerId` INT NOT NULL,
            `SavingState` INT NULL,
            `RowGuid` CHAR(36) NOT NULL DEFAULT (UUID()),
            `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`NodeId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;

    -- Create indexes on the Nodes table if they do not exist
    IF NOT EXISTS (
        SELECT INDEX_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Nodes' AND INDEX_NAME = 'IX_Nodes_Path'
    ) THEN
        CREATE UNIQUE INDEX `IX_Nodes_Path` ON `Nodes` (`Path`, `NodeId`);
    END IF;

    IF NOT EXISTS (
        SELECT INDEX_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Nodes' AND INDEX_NAME = 'IX_Nodes_ParentNodeId'
    ) THEN
        CREATE INDEX `IX_Nodes_ParentNodeId` ON `Nodes` (`ParentNodeId`);
    END IF;

    IF NOT EXISTS (
        SELECT INDEX_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Nodes' AND INDEX_NAME = 'IX_Nodes_NodeTypeId'
    ) THEN
        CREATE INDEX `IX_Nodes_NodeTypeId` ON `Nodes` (`NodeTypeId`);
    END IF;
END $$

DELIMITER ;

-- Create the Versions table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateVersionsTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Versions'
    ) THEN
        CREATE TABLE `Versions` (
            `VersionId` INT NOT NULL AUTO_INCREMENT,
            `NodeId` INT NOT NULL,
            `MajorNumber` SMALLINT NOT NULL,
            `MinorNumber` SMALLINT NOT NULL,
            `CreationDate` DATETIME(6) NOT NULL,
            `CreatedById` INT NOT NULL,
            `ModificationDate` DATETIME(6) NOT NULL,
            `ModifiedById` INT NOT NULL,
            `Status` SMALLINT NOT NULL DEFAULT 1,
            `IndexDocument` LONGTEXT NULL,
            `ChangedData` LONGTEXT NULL,
            `DynamicProperties` LONGTEXT NULL,
            `ContentListProperties` LONGTEXT NULL,
            `RowGuid` CHAR(36) NOT NULL DEFAULT (UUID()),
            `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (`VersionId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

-- Create the NodeTypes table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateNodeTypesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'NodeTypes'
    ) THEN
        CREATE TABLE `NodeTypes` (
            `NodeTypeId` INT NOT NULL AUTO_INCREMENT,
            `ParentId` INT NULL,
            `Name` VARCHAR(450) NOT NULL,
            `ClassName` VARCHAR(450) NULL,
            `Properties` LONGTEXT NOT NULL,
            PRIMARY KEY (`NodeTypeId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;

    -- Create the non-clustered index on ParentId
    IF NOT EXISTS (
        SELECT INDEX_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'NodeTypes' AND INDEX_NAME = 'ix_parentid'
    ) THEN
        CREATE INDEX `ix_parentid` ON `NodeTypes` (`ParentId`);
    END IF;
END $$

DELIMITER ;

-- Create the ContentListTypes table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateContentListTypesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ContentListTypes'
    ) THEN
        CREATE TABLE `ContentListTypes` (
            `ContentListTypeId` INT NOT NULL AUTO_INCREMENT,
            `Name` VARCHAR(450) NOT NULL,
            `Properties` LONGTEXT NOT NULL,
            PRIMARY KEY (`ContentListTypeId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

-- Create the PropertyTypes table if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreatePropertyTypesTable`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'PropertyTypes'
    ) THEN
        CREATE TABLE `PropertyTypes` (
            `PropertyTypeId` INT NOT NULL AUTO_INCREMENT,
            `Name` VARCHAR(450) NOT NULL,
            `DataType` VARCHAR(10) NOT NULL,
            `Mapping` INT NOT NULL,
            `IsContentListProperty` TINYINT NOT NULL DEFAULT 0,
            PRIMARY KEY (`PropertyTypeId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    END IF;
END $$

DELIMITER ;

------------------------------------------------              --------------------------------------------------------------
------------------------------------------------ CREATE VIEWS --------------------------------------------------------------
------------------------------------------------              --------------------------------------------------------------

-- Create the NodeInfoView view if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateNodeInfoView`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.VIEWS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'NodeInfoView'
    ) THEN
        CREATE VIEW `NodeInfoView` AS
        SELECT 
            N.NodeId,
            T.Name AS `Type`,
            N.Name,
            N.Path,
            N.LockedById,
            V.VersionId,
            CONCAT(
                CAST(V.MajorNumber AS CHAR), '.', 
                CAST(V.MinorNumber AS CHAR), '.', 
                CASE V.Status
                    WHEN 1 THEN 'A'
                    WHEN 2 THEN 'L'
                    WHEN 4 THEN 'D'
                    WHEN 8 THEN 'R'
                    WHEN 16 THEN 'P'
                    ELSE ''
                END
            ) AS Version,
            CASE V.VersionId 
                WHEN N.LastMajorVersionId THEN 'TRUE' 
                ELSE 'false' 
            END AS LastPub,
            CASE V.VersionId 
                WHEN N.LastMinorVersionId THEN 'TRUE' 
                ELSE 'false' 
            END AS LastWork
        FROM 
            Versions AS V
        INNER JOIN 
            Nodes AS N ON V.NodeId = N.NodeId
        INNER JOIN 
            NodeTypes AS T ON N.NodeTypeId = T.NodeTypeId;
    END IF;
END $$

DELIMITER ;

-- Create the ReferencesInfoView view if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreateReferencesInfoView`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.VIEWS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ReferencesInfoView'
    ) THEN
        CREATE VIEW `ReferencesInfoView` AS
        -- ReferenceProperties
        SELECT 
            Nodes.Name AS SrcName,
            CONCAT('V', CAST(Versions.MajorNumber AS CHAR), '.', CAST(Versions.MinorNumber AS CHAR)) AS SrcVer,
            Slots.Name AS RelType,
            RefNodes.Name AS TargetName,
            Nodes.NodeId AS SrcId,
            RefNodes.NodeId AS TargetId,
            Nodes.Path AS SrcPath,
            RefNodes.Path AS TargetPath
        FROM 
            ReferenceProperties AS Refs
        INNER JOIN 
            Versions ON Refs.VersionId = Versions.VersionId
        INNER JOIN 
            Nodes ON Versions.NodeId = Nodes.NodeId
        INNER JOIN 
            Nodes AS RefNodes ON Refs.ReferredNodeId = RefNodes.NodeId
        INNER JOIN 
            PropertyTypes AS Slots ON Refs.PropertyTypeId = Slots.PropertyTypeId

        UNION ALL

        -- Parent
        SELECT 
            Nodes.Name AS SrcName,
            'V*.*' AS SrcVer,
            'Parent' AS RelType,
            RefNodes.Name AS TargetName,
            Nodes.NodeId AS SrcId,
            RefNodes.NodeId AS TargetId,
            Nodes.Path AS SrcPath,
            RefNodes.Path AS TargetPath
        FROM 
            Nodes
        INNER JOIN 
            Nodes AS RefNodes ON Nodes.ParentNodeId = RefNodes.NodeId

        UNION ALL

        -- LockedById
        SELECT 
            Nodes.Name AS SrcName,
            'V*.*' AS SrcVer,
            'LockedById' AS RelType,
            RefNodes.Name AS TargetName,
            Nodes.NodeId AS SrcId,
            RefNodes.NodeId AS TargetId,
            Nodes.Path AS SrcPath,
            RefNodes.Path AS TargetPath
        FROM 
            Nodes
        INNER JOIN 
            Nodes AS RefNodes ON Nodes.LockedById = RefNodes.NodeId

        UNION ALL

        -- CreatedById
        SELECT 
            Nodes.Name AS SrcName,
            CONCAT('V', CAST(Versions.MajorNumber AS CHAR), '.', CAST(Versions.MinorNumber AS CHAR)) AS SrcVer,
            'CreatedById' AS RelType,
            RefNodes.Name AS TargetName,
            Nodes.NodeId AS SrcId,
            RefNodes.NodeId AS TargetId,
            Nodes.Path AS SrcPath,
            RefNodes.Path AS TargetPath
        FROM 
            Nodes
        INNER JOIN 
            Versions ON Nodes.NodeId = Versions.NodeId
        INNER JOIN 
            Nodes AS RefNodes ON Versions.CreatedById = RefNodes.NodeId

        UNION ALL

        -- ModifiedById
        SELECT 
            Nodes.Name AS SrcName,
            CONCAT('V', CAST(Versions.MajorNumber AS CHAR), '.', CAST(Versions.MinorNumber AS CHAR)) AS SrcVer,
            'ModifiedById' AS RelType,
            RefNodes.Name AS TargetName,
            Nodes.NodeId AS SrcId,
            RefNodes.NodeId AS TargetId,
            Nodes.Path AS SrcPath,
            RefNodes.Path AS TargetPath
        FROM 
            Nodes
        INNER JOIN 
            Versions ON Nodes.NodeId = Versions.NodeId
        INNER JOIN 
            Nodes AS RefNodes ON Versions.ModifiedById = RefNodes.NodeId;
    END IF;
END $$

DELIMITER ;

-- Create the PermissionInfoView view if it does not exist
DELIMITER $$

CREATE PROCEDURE `CreatePermissionInfoView`()
BEGIN
    IF NOT EXISTS (
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.VIEWS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'PermissionInfoView'
    ) THEN
        CREATE VIEW `PermissionInfoView` AS
        SELECT 
            n.Path,
            e1.IsInherited,
            i.Path AS IdentityPath,
            e.LocalOnly,
            CONCAT(
                CASE (DenyBits & 0x8000000000000000)
                    WHEN 0 THEN CASE (AllowBits & 0x8000000000000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                CASE (DenyBits & 0x4000000000000000)
                    WHEN 0 THEN CASE (AllowBits & 0x4000000000000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                CASE (DenyBits & 0x2000000000000000)
                    WHEN 0 THEN CASE (AllowBits & 0x2000000000000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                CASE (DenyBits & 0x1000000000000000)
                    WHEN 0 THEN CASE (AllowBits & 0x1000000000000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                -- Repeat for other bitwise conditions similarly
                ...
            ) AS CustomBits,
            CONCAT(
                CASE (DenyBits & 0x80000000)
                    WHEN 0 THEN CASE (AllowBits & 0x80000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                CASE (DenyBits & 0x40000000)
                    WHEN 0 THEN CASE (AllowBits & 0x40000000) WHEN 0 THEN '_' ELSE '+' END
                    ELSE '-'
                END,
                -- Repeat for other bitwise conditions similarly
                ...
            ) AS SystemBits,
            e.EFEntityId,
            e.IdentityId,
            e.AllowBits,
            e.DenyBits,
            -- Repeat individual permissions like See, Pre, PWa, etc., using CONCAT and CASE logic
            ...
        FROM 
            EFEntries e
        JOIN EFEntities e1 ON e.EFEntityId = e1.Id
        JOIN Nodes n ON e.EFEntityId = n.NodeId
        JOIN Nodes i ON e.IdentityId = i.NodeId;
    END IF;
END $$

DELIMITER ;

------------------------------------------------                    --------------------------------------------------------------
------------------------------------------------ CREATE CONSTRAINTS --------------------------------------------------------------
------------------------------------------------                    --------------------------------------------------------------

-- Add foreign key constraints for the BinaryProperties table

-- FK_BinaryProperties_PropertyTypes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_BinaryProperties_PropertyTypes'
) THEN
    ALTER TABLE `BinaryProperties`
    ADD CONSTRAINT `FK_BinaryProperties_PropertyTypes`
    FOREIGN KEY (`PropertyTypeId`) REFERENCES `PropertyTypes` (`PropertyTypeId`);
END IF;

-- FK_BinaryProperties_Versions
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_BinaryProperties_Versions'
) THEN
    ALTER TABLE `BinaryProperties`
    ADD CONSTRAINT `FK_BinaryProperties_Versions`
    FOREIGN KEY (`VersionId`) REFERENCES `Versions` (`VersionId`);
END IF;

-- FK_BinaryProperties_Files
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_BinaryProperties_Files'
) THEN
    ALTER TABLE `BinaryProperties`
    ADD CONSTRAINT `FK_BinaryProperties_Files`
    FOREIGN KEY (`FileId`) REFERENCES `Files` (`FileId`);
END IF;

-- Add foreign key constraints for the Nodes table

-- FK_Nodes_LockedBy
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Nodes_LockedBy'
) THEN
    ALTER TABLE `Nodes`
    ADD CONSTRAINT `FK_Nodes_LockedBy`
    FOREIGN KEY (`LockedById`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Nodes_Parent
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Nodes_Parent'
) THEN
    ALTER TABLE `Nodes`
    ADD CONSTRAINT `FK_Nodes_Parent`
    FOREIGN KEY (`ParentNodeId`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Nodes_NodeTypes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Nodes_NodeTypes'
) THEN
    ALTER TABLE `Nodes`
    ADD CONSTRAINT `FK_Nodes_NodeTypes`
    FOREIGN KEY (`NodeTypeId`) REFERENCES `NodeTypes` (`NodeTypeId`);
END IF;

-- Add foreign key constraints for the ReferenceProperties table

-- FK_ReferenceProperties_PropertyTypes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_ReferenceProperties_PropertyTypes'
) THEN
    ALTER TABLE `ReferenceProperties`
    ADD CONSTRAINT `FK_ReferenceProperties_PropertyTypes`
    FOREIGN KEY (`PropertyTypeId`) REFERENCES `PropertyTypes` (`PropertyTypeId`);
END IF;

-- Add foreign key constraints for the NodeTypes table

-- FK_NodeTypes_NodeTypes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_NodeTypes_NodeTypes'
) THEN
    ALTER TABLE `NodeTypes`
    ADD CONSTRAINT `FK_NodeTypes_NodeTypes`
    FOREIGN KEY (`ParentId`) REFERENCES `NodeTypes` (`NodeTypeId`);
END IF;

-- Add foreign key constraints for the LongTextProperties table

-- FK_LongTextProperties_PropertyTypes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_LongTextProperties_PropertyTypes'
) THEN
    ALTER TABLE `LongTextProperties`
    ADD CONSTRAINT `FK_LongTextProperties_PropertyTypes`
    FOREIGN KEY (`PropertyTypeId`) REFERENCES `PropertyTypes` (`PropertyTypeId`);
END IF;

-- FK_LongTextProperties_Versions
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_LongTextProperties_Versions'
) THEN
    ALTER TABLE `LongTextProperties`
    ADD CONSTRAINT `FK_LongTextProperties_Versions`
    FOREIGN KEY (`VersionId`) REFERENCES `Versions` (`VersionId`);
END IF;

-- Add foreign key constraints for the Versions table

-- FK_Versions_Nodes
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Versions_Nodes'
) THEN
    ALTER TABLE `Versions`
    ADD CONSTRAINT `FK_Versions_Nodes`
    FOREIGN KEY (`NodeId`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Versions_Nodes_CreatedBy
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Versions_Nodes_CreatedBy'
) THEN
    ALTER TABLE `Versions`
    ADD CONSTRAINT `FK_Versions_Nodes_CreatedBy`
    FOREIGN KEY (`CreatedById`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Versions_Nodes_ModifiedBy
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Versions_Nodes_ModifiedBy'
) THEN
    ALTER TABLE `Versions`
    ADD CONSTRAINT `FK_Versions_Nodes_ModifiedBy`
    FOREIGN KEY (`ModifiedById`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Nodes_Nodes_CreatedById
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Nodes_Nodes_CreatedById'
) THEN
    ALTER TABLE `Nodes`
    ADD CONSTRAINT `FK_Nodes_Nodes_CreatedById`
    FOREIGN KEY (`CreatedById`) REFERENCES `Nodes` (`NodeId`);
END IF;

-- FK_Nodes_Nodes_ModifiedById
IF NOT EXISTS (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_Nodes_Nodes_ModifiedById'
) THEN
    ALTER TABLE `Nodes`
    ADD CONSTRAINT `FK_Nodes_Nodes_ModifiedById`
    FOREIGN KEY (`ModifiedById`) REFERENCES `Nodes` (`NodeId`);
END IF;

CREATE TABLE `JournalItems` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `When` DATETIME(6) NOT NULL,
    `Wherewith` NVARCHAR(450) NOT NULL,
    `What` NVARCHAR(100) NOT NULL,
    `Who` NVARCHAR(200) NOT NULL,
    `RowGuid` CHAR(36) NOT NULL DEFAULT (UUID()),
    `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `NodeId` INT NOT NULL,
    `DisplayName` NVARCHAR(450) NOT NULL,
    `NodeTypeName` NVARCHAR(100) NOT NULL,
    `SourcePath` NVARCHAR(450) NULL,
    `TargetPath` NVARCHAR(450) NULL,
    `TargetDisplayName` NVARCHAR(450) NULL,
    `Hidden` BIT NOT NULL,
    `Details` NVARCHAR(450) NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX `IX_JournalItems` ON `JournalItems` (`When` DESC, `Wherewith` ASC);

-- Create the LogEntries table if it does not exist
CREATE TABLE IF NOT EXISTS `LogEntries` (
    `LogId` INT NOT NULL AUTO_INCREMENT,
    `EventId` INT NOT NULL,
    `Category` NVARCHAR(50) DEFAULT NULL,
    `Priority` INT NOT NULL,
    `Severity` VARCHAR(30) NOT NULL,
    `Title` NVARCHAR(256) DEFAULT NULL,
    `ContentId` INT DEFAULT NULL,
    `ContentPath` NVARCHAR(450) DEFAULT NULL,
    `UserName` NVARCHAR(450) DEFAULT NULL,
    `LogDate` DATETIME(6) NOT NULL,
    `MachineName` VARCHAR(32) DEFAULT NULL,
    `AppDomainName` VARCHAR(512) DEFAULT NULL,
    `ProcessID` VARCHAR(256) DEFAULT NULL,
    `ProcessName` VARCHAR(512) DEFAULT NULL,
    `ThreadName` VARCHAR(512) DEFAULT NULL,
    `Win32ThreadId` VARCHAR(128) DEFAULT NULL,
    `Message` NVARCHAR(1500) DEFAULT NULL,
    `FormattedMessage` LONGTEXT DEFAULT NULL,
    `RowGuid` CHAR(36) NOT NULL DEFAULT (UUID()),
    `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`LogId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `IndexingActivities` (
    `IndexingActivityId` INT NOT NULL AUTO_INCREMENT,
    `ActivityType` VARCHAR(50) NOT NULL,
    `CreationDate` DATETIME(6) NOT NULL,
    `RunningState` VARCHAR(10) NOT NULL,
    `LockTime` DATETIME(6) DEFAULT NULL,
    `NodeId` INT NOT NULL,
    `VersionId` INT NOT NULL,
    `Path` NVARCHAR(450) NOT NULL,
    `VersionTimestamp` BIGINT DEFAULT NULL,
    `Extension` LONGTEXT DEFAULT NULL,
    PRIMARY KEY (`IndexingActivityId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `WorkflowNotification` (
    `NotificationId` INT NOT NULL AUTO_INCREMENT,
    `NodeId` INT NOT NULL,
    `WorkflowInstanceId` CHAR(36) NOT NULL, -- For uniqueidentifier mapped to CHAR(36)
    `WorkflowNodePath` NVARCHAR(450) NOT NULL,
    `BookmarkName` VARCHAR(50) NOT NULL,
    PRIMARY KEY (`NotificationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `SchemaModification` (
    `SchemaModificationId` INT NOT NULL AUTO_INCREMENT,
    `ModificationDate` DATETIME(6) NOT NULL,
    `LockToken` VARCHAR(50) DEFAULT NULL,
    `Timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`SchemaModificationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `Packages` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `PackageType` VARCHAR(50) NOT NULL,
    `ComponentId` NVARCHAR(450) DEFAULT NULL,
    `ComponentVersion` VARCHAR(50) DEFAULT NULL,
    `ReleaseDate` DATETIME(6) NOT NULL,
    `ExecutionDate` DATETIME(6) NOT NULL,
    `ExecutionResult` VARCHAR(50) NOT NULL,
    `ExecutionError` LONGTEXT DEFAULT NULL, -- VARCHAR(MAX) converted to LONGTEXT
    `Description` NVARCHAR(1000) DEFAULT NULL,
    `Manifest` LONGTEXT DEFAULT NULL, -- NVARCHAR(MAX) converted to LONGTEXT
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `TreeLocks` (
    `TreeLockId` INT NOT NULL AUTO_INCREMENT,
    `Path` NVARCHAR(450) NOT NULL,
    `LockedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`TreeLockId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `AccessTokens` (
    `AccessTokenId` INT NOT NULL AUTO_INCREMENT,
    `Value` NVARCHAR(1000) NOT NULL,
    `UserId` INT NOT NULL,
    `ContentId` INT DEFAULT NULL,
    `Feature` NVARCHAR(1000) DEFAULT NULL,
    `CreationDate` DATETIME(6) NOT NULL,
    `ExpirationDate` DATETIME(6) NOT NULL,
    PRIMARY KEY (`AccessTokenId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `SharedLocks` (
    `SharedLockId` INT NOT NULL AUTO_INCREMENT,
    `ContentId` INT NOT NULL,
    `Lock` NVARCHAR(1000) NOT NULL,
    `CreationDate` DATETIME(6) NOT NULL,
    PRIMARY KEY (`SharedLockId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX `ix_version_id` ON `BinaryProperties` (`VersionId` ASC);

CREATE INDEX `ix_file_id` ON `BinaryProperties` (`FileId` ASC);

CREATE INDEX `ix_version_id` ON `ReferenceProperties` (`VersionId` ASC);

CREATE INDEX `ix_version_id` ON `LongTextProperties` (`VersionId` ASC);

CREATE INDEX `ix_Versions_NodeId` ON `Versions` (`NodeId`);

CREATE INDEX `ix_Versions_NodeId_MinorNumber_MajorNumber_Status`
ON `Versions` (`NodeId`, `MinorNumber`, `Status`);

CREATE INDEX `ix_name` ON `NodeTypes` (`Name` ASC)
INCLUDE (`NodeTypeId`);

CREATE INDEX `ix_name` ON `PropertyTypes` (`Name` ASC)
INCLUDE (`PropertyTypeId`);

--============================== Switch off the foreign keys ==============================
SET FOREIGN_KEY_CHECKS = 0;
--=========================================================================================