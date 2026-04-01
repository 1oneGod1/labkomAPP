-- ==============================================
-- LABKOM - UPDATE Password Hash Siswa Dummy
-- Jalankan file ini di phpMyAdmin SETELAH labkom.sql
-- ==============================================

USE labkom_db;

-- NIS: 10001 | Password: budi123
UPDATE students SET password_hash = '$2b$10$BbjaHINug2BcVzuVSZ45HeGDUlmSHfktq2lWxDRsrCW.O2Eu7n0zK' WHERE nis = '10001';

-- NIS: 10002 | Password: siti456
UPDATE students SET password_hash = '$2b$10$E7T/yJc2tattYfy418i0SOzZh6GA4dFp20l6rQtm4I9V7YS8FZeXO' WHERE nis = '10002';

-- NIS: 10003 | Password: ahmad789
UPDATE students SET password_hash = '$2b$10$zMK30CcP0Xe8kxjGzvRv.OTua1Bq8VaYqwh9L3qFSugosT3aml62O' WHERE nis = '10003';

-- NIS: 10004 | Password: dewi321 (akun nonaktif)
UPDATE students SET password_hash = '$2b$10$MSeW1Dk4kiEiXfgVIK7BYehmvZs3rKXszKRPOpzMLhF6E6B9yoMa.' WHERE nis = '10004';
