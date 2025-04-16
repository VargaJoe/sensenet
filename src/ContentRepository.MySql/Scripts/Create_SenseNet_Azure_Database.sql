/****** DROP DATABASE ******/
DROP DATABASE IF EXISTS `$(INITIALCATALOG)`;

/****** CREATE DATABASE ******/
CREATE DATABASE `$(INITIALCATALOG)` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;

/****** Configure the database (MySQL does not have direct ALTER DATABASE options as in SQL Server) ******/
/* Note: MySQL database-level settings like storage engines and charset are generally set during creation.
   Additional configurations, if needed, are handled at the server or table level. */