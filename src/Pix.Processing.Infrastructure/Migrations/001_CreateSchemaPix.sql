IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'pix')
BEGIN
    EXEC('CREATE SCHEMA [pix]');
END
