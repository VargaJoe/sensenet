/****** DROP DATABASE: [SenseNetContentRepository] ******/
DROP DATABASE IF EXISTS `$(INITIALCATALOG)`;

/****** CREATE DATABASE: [SenseNetContentRepository] ******/
CREATE DATABASE `$(INITIALCATALOG)` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;

/****** Configure the database (MySQL lacks ALTER DATABASE options as in SQL Server) ******/
/* Note: MySQL database-level settings like charset and collation are defined during creation.
   Additional configurations (e.g., isolation levels) are typically handled at the session or server level. */