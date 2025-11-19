"""
Unit tests for config module.
"""

import unittest
import tempfile
import yaml
from pathlib import Path
from lib.config import load_config, get_default_config, AnalyzerConfig


class TestConfigLoading(unittest.TestCase):
    """Tests for configuration loading."""

    def test_default_config(self):
        """Test that default config has expected values"""
        config = get_default_config()

        self.assertIsInstance(config, AnalyzerConfig)
        self.assertEqual(config.zero_threshold, 0.05)
        self.assertEqual(config.thresholds, [0.3, 0.5, 0.4])
        self.assertIsNone(config.workers)
        self.assertIsNone(config.exchanges)

    def test_load_valid_config(self):
        """Test loading a valid config file"""
        # Create temporary config file
        config_data = {
            'paths': {
                'data_directory': '/test/data',
                'output_directory': '/test/output'
            },
            'analysis': {
                'zero_threshold': 0.1,
                'thresholds': [0.2, 0.4, 0.6]
            },
            'performance': {
                'workers': 8,
                'chunk_size': 2
            },
            'exchanges': ['Binance', 'Bybit'],
            'date_range': {
                'start_date': '2025-01-01',
                'end_date': '2025-12-31'
            }
        }

        with tempfile.NamedTemporaryFile(mode='w', suffix='.yaml', delete=False) as f:
            yaml.dump(config_data, f)
            config_path = Path(f.name)

        try:
            config = load_config(config_path)

            self.assertEqual(config.data_directory, '/test/data')
            self.assertEqual(config.output_directory, '/test/output')
            self.assertEqual(config.zero_threshold, 0.1)
            self.assertEqual(config.thresholds, [0.2, 0.4, 0.6])
            self.assertEqual(config.workers, 8)
            self.assertEqual(config.chunk_size, 2)
            self.assertEqual(config.exchanges, ['Binance', 'Bybit'])
            self.assertEqual(config.start_date, '2025-01-01')
            self.assertEqual(config.end_date, '2025-12-31')
        finally:
            config_path.unlink()

    def test_load_partial_config(self):
        """Test loading config with missing sections (should use defaults)"""
        config_data = {
            'paths': {
                'data_directory': '/custom/path'
            }
        }

        with tempfile.NamedTemporaryFile(mode='w', suffix='.yaml', delete=False) as f:
            yaml.dump(config_data, f)
            config_path = Path(f.name)

        try:
            config = load_config(config_path)

            # Custom value
            self.assertEqual(config.data_directory, '/custom/path')

            # Default values
            self.assertEqual(config.zero_threshold, 0.05)
            self.assertEqual(config.thresholds, [0.3, 0.5, 0.4])
        finally:
            config_path.unlink()

    def test_missing_config_file(self):
        """Test that missing config file raises FileNotFoundError"""
        with self.assertRaises(FileNotFoundError):
            load_config(Path('/nonexistent/config.yaml'))

    def test_config_dataclass_attributes(self):
        """Test that AnalyzerConfig has all required attributes"""
        config = get_default_config()

        # Check all expected attributes exist
        self.assertTrue(hasattr(config, 'data_directory'))
        self.assertTrue(hasattr(config, 'output_directory'))
        self.assertTrue(hasattr(config, 'zero_threshold'))
        self.assertTrue(hasattr(config, 'thresholds'))
        self.assertTrue(hasattr(config, 'workers'))
        self.assertTrue(hasattr(config, 'chunk_size'))
        self.assertTrue(hasattr(config, 'exchanges'))
        self.assertTrue(hasattr(config, 'start_date'))
        self.assertTrue(hasattr(config, 'end_date'))


if __name__ == '__main__':
    unittest.main()
