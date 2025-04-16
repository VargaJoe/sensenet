/****** DROP DATABASE: [{DatabaseName}] ******/
DROP DATABASE IF EXISTS `{DatabaseName}`;

/****** CREATE DATABASE: [{DatabaseName}] ******/
CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;

/****** Configure the database (MySQL lacks direct ALTER DATABASE options as in SQL Server) ******/
/* Note: MySQL database-level settings such as charset and collation are defined during creation.
   Additional configurations (e.g., isolation levels) are typically handled at the session or server level. */