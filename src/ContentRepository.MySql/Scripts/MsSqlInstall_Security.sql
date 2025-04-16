-- Drop foreign key constraints if they exist
ALTER TABLE `EFEntries` DROP FOREIGN KEY IF EXISTS `FK_dbo.EFEntries_dbo.EFEntities_EFEntityId`;
ALTER TABLE `EFEntities` DROP FOREIGN KEY IF EXISTS `FK_dbo.EFEntities_dbo.EFEntities_ParentId`;

-- Drop tables if they exist
DROP TABLE IF EXISTS `EFMessages`;
DROP TABLE IF EXISTS `EFMemberships`;
DROP TABLE IF EXISTS `EFEntries`;
DROP TABLE IF EXISTS `EFEntities`;

-- Create table EFEntities
CREATE TABLE IF NOT EXISTS `EFEntities` (
    `Id` INT NOT NULL,
    `OwnerId` INT NULL,
    `ParentId` INT NULL,
    `IsInherited` BIT NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

-- Create table EFEntries
CREATE TABLE IF NOT EXISTS `EFEntries` (
    `EFEntityId` INT NOT NULL,
    `EntryType` INT NOT NULL,
    `IdentityId` INT NOT NULL,
    `LocalOnly` BIT NOT NULL,
    `AllowBits` BIGINT NOT NULL,
    `DenyBits` BIGINT NOT NULL,
    PRIMARY KEY (`EFEntityId`, `EntryType`, `IdentityId`, `LocalOnly`)
) ENGINE=InnoDB;

-- Create table EFMemberships
CREATE TABLE IF NOT EXISTS `EFMemberships` (
    `GroupId` INT NOT NULL,
    `MemberId` INT NOT NULL,
    `IsUser` BIT NOT NULL,
    PRIMARY KEY (`GroupId`, `MemberId`)
) ENGINE=InnoDB;

-- Create table EFMessages
CREATE TABLE IF NOT EXISTS `EFMessages` (
    `Id` INT AUTO_INCREMENT NOT NULL,
    `SavedBy` NVARCHAR(255) NULL,
    `SavedAt` DATETIME NOT NULL,
    `ExecutionState` NVARCHAR(255) NULL,
    `LockedBy` NVARCHAR(255) NULL,
    `LockedAt` DATETIME NULL,
    `Body` LONGBLOB NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

-- Create indexes
CREATE INDEX `IX_ParentId` ON `EFEntities` (`ParentId`);
CREATE INDEX `IX_EFEntityId` ON `EFEntries` (`EFEntityId`);

-- Add foreign key constraints
ALTER TABLE `EFEntities`
    ADD CONSTRAINT `FK_dbo.EFEntities_dbo.EFEntities_ParentId`
    FOREIGN KEY (`ParentId`)
    REFERENCES `EFEntities` (`Id`);

ALTER TABLE `EFEntries`
    ADD CONSTRAINT `FK_dbo.EFEntries_dbo.EFEntities_EFEntityId`
    FOREIGN KEY (`EFEntityId`)
    REFERENCES `EFEntities` (`Id`);