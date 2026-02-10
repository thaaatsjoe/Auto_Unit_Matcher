-- Seed data for AUM database
-- This data is inserted on initial database creation

-- Insert default test employee
INSERT OR IGNORE INTO users (employee_number, employee_name, is_active)
VALUES ('123456', 'Test User', 1);
