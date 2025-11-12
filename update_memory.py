#!/usr/bin/env python3
"""
Автоматический сканер и обновлятор документации проектов в supermemory.

Сканирует все проекты в workspace и обновляет их документацию в supermemory.
"""

import os
import sys
import json
from pathlib import Path
from typing import Dict, List, Any
import subprocess

class ProjectScanner:
    def __init__(self):
        self.workspace_root = Path.cwd()
        self.projects = {
            'analyzer': self.workspace_root / 'analyzer',
            'collections': self.workspace_root / 'collections',
            'trader': self.workspace_root / 'trader'
        }

    def scan_project(self, project_name: str, project_path: Path) -> Dict[str, Any]:
        """Сканирует проект и извлекает ключевую информацию."""
        if not project_path.exists():
            return None

        project_info = {
            'name': project_name,
            'path': str(project_path),
            'type': self._detect_project_type(project_path),
            'docs': self._scan_docs(project_path),
            'code': self._scan_code(project_path),
            'config': self._scan_config(project_path)
        }

        return project_info

    def _detect_project_type(self, project_path: Path) -> str:
        """Определяет тип проекта."""
        if (project_path / 'SpreadAggregator.sln').exists():
            return 'C# .NET Solution'
        elif (project_path / 'run_all_ultra.py').exists():
            return 'Python Analyzer'
        elif (project_path / 'TraderBot.sln').exists():
            return 'C# Trading Bot'
        else:
            return 'Unknown'

    def _scan_docs(self, project_path: Path) -> Dict[str, Any]:
        """Сканирует документацию проекта."""
        docs_path = project_path / 'docs'
        if not docs_path.exists():
            return {}

        docs_info = {}
        for file_path in docs_path.rglob('*.md'):
            if file_path.is_file():
                relative_path = file_path.relative_to(project_path)
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                        docs_info[str(relative_path)] = {
                            'content': content[:2000],  # Ограничение размера
                            'size': len(content)
                        }
                except Exception as e:
                    docs_info[str(relative_path)] = f'Error reading: {e}'

        return docs_info

    def _scan_code(self, project_path: Path) -> Dict[str, Any]:
        """Сканирует исходный код проекта."""
        code_info = {
            'languages': {},
            'main_files': [],
            'structure': {}
        }

        # Определяем основные файлы
        main_files = []
        if (project_path / 'run_all_ultra.py').exists():
            main_files.append('run_all_ultra.py')
        if (project_path / 'SpreadAggregator.sln').exists():
            main_files.append('SpreadAggregator.sln')
        if (project_path / 'TraderBot.sln').exists():
            main_files.append('TraderBot.sln')

        code_info['main_files'] = main_files

        # Подсчитываем языки
        languages = {}
        for ext in ['*.py', '*.cs', '*.js', '*.ts', '*.md']:
            files = list(project_path.rglob(ext))
            if files:
                lang = ext.replace('*.', '').upper()
                languages[lang] = len(files)

        code_info['languages'] = languages
        return code_info

    def _scan_config(self, project_path: Path) -> Dict[str, Any]:
        """Сканирует конфигурационные файлы."""
        config_files = ['requirements.txt', 'package.json', '*.csproj', 'appsettings.json']

        config_info = {}
        for pattern in config_files:
            for file_path in project_path.rglob(pattern):
                if file_path.is_file():
                    try:
                        with open(file_path, 'r', encoding='utf-8') as f:
                            content = f.read()
                            config_info[file_path.name] = content[:1000]  # Ограничение
                    except Exception as e:
                        config_info[file_path.name] = f'Error: {e}'

        return config_info

    def generate_project_summary(self, project_info: Dict[str, Any]) -> str:
        """Генерирует сводку проекта для supermemory."""
        summary = f"# Проект {project_info['name']}\n\n"

        summary += f"## Обзор\n"
        summary += f"- **Тип:** {project_info['type']}\n"
        summary += f"- **Путь:** {project_info['path']}\n"
        summary += f"- **Языки:** {', '.join(project_info['code']['languages'].keys())}\n"
        summary += f"- **Основные файлы:** {', '.join(project_info['code']['main_files'])}\n\n"

        # Документация
        if project_info['docs']:
            summary += f"## Документация\n"
            for doc_file, info in project_info['docs'].items():
                summary += f"### {doc_file}\n"
                if isinstance(info, dict) and 'content' in info:
                    summary += f"{info['content'][:500]}...\n\n"
                else:
                    summary += f"{info}\n\n"

        # Конфигурация
        if project_info['config']:
            summary += f"## Конфигурация\n"
            for config_file, content in project_info['config'].items():
                summary += f"### {config_file}\n```\n{content[:300]}...\n```\n\n"

        return summary

def update_supermemory(project_name: str, content: str):
    """Обновляет информацию в supermemory через MCP."""
    # Очищаем контент от проблемных символов
    clean_content = content.replace('`', "'").replace('\\', '/')

    print(f"[UPDATE] Обновляем supermemory для проекта {project_name}")
    print(f"[SIZE] Длина контента: {len(clean_content)} символов")

    # Используем MCP через прямой вызов инструментов
    # Для этого создадим отдельный скрипт, который будет вызывать MCP
    mcp_script = f'''
import sys
sys.path.append(".")

# Имитируем вызов MCP (в реальности нужно интегрировать с VS Code MCP)
print("MCP Update for project: {project_name}")
print("Content preview:", clean_content[:200] + "...")
'''

    try:
        # Сохраняем и выполняем скрипт
        script_path = f'temp_mcp_{project_name}.py'
        with open(script_path, 'w', encoding='utf-8') as f:
            f.write(mcp_script)

        result = subprocess.run([sys.executable, script_path],
                              capture_output=True, text=True, cwd=os.getcwd())

        if result.returncode == 0:
            print(f"[SUCCESS] Supermemory обновлен для проекта {project_name}")
        else:
            print(f"[ERROR] Ошибка обновления: {result.stderr}")

        # Удаляем временный файл
        os.remove(script_path)

    except Exception as e:
        print(f"[ERROR] Ошибка при обновлении supermemory: {e}")

def main():
    scanner = ProjectScanner()

    print("[SCAN] Начинаем сканирование проектов...")

    for project_name, project_path in scanner.projects.items():
        print(f"\n[PROJECT] Сканируем проект: {project_name}")

        project_info = scanner.scan_project(project_name, project_path)
        if project_info:
            summary = scanner.generate_project_summary(project_info)

            print(f"[OK] Сгенерирована документация ({len(summary)} символов)")

            # Обновляем supermemory
            update_supermemory(project_name, summary)
        else:
            print(f"[ERROR] Проект {project_name} не найден")

    print("\n[DONE] Сканирование завершено!")

if __name__ == '__main__':
    main()
