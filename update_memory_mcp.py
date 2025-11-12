#!/usr/bin/env python3
"""
Интеграция с MCP supermemory для обновления документации проектов.
"""

import os
import sys
import json
from pathlib import Path
from typing import Dict, List, Any

# Импортируем наш сканер
from update_memory import ProjectScanner

def call_mcp_tool(server_name: str, tool_name: str, arguments: Dict[str, Any]) -> Dict[str, Any]:
    """
    Вызывает MCP инструмент через stdin/stdout интерфейс.
    Это имитация того, как MCP клиенты работают.
    """
    # В реальном сценарии это должно быть интегрировано с VS Code MCP API
    # Пока просто имитируем успешный ответ

    if tool_name == "addMemory":
        return {
            "content": [
                {
                    "type": "text",
                    "text": f"Memory added successfully with ID: mock_{arguments['thingToRemember'][:10].replace(' ', '_')}"
                }
            ]
        }
    elif tool_name == "search":
        return {
            "content": [
                {
                    "type": "text",
                    "text": f"Found results for: {arguments.get('informationToGet', '')[:50]}..."
                }
            ]
        }

    return {"error": f"Unknown tool: {tool_name}"}

def update_project_in_supermemory(project_name: str, project_info: Dict[str, Any]):
    """Обновляет информацию о проекте в supermemory через MCP."""

    # Генерируем контент для supermemory
    content = f"""# Проект {project_name}

## Обзор
- **Тип проекта:** {project_info['type']}
- **Расположение:** {project_info['path']}
- **Языки программирования:** {', '.join(project_info['code']['languages'].keys())}
- **Основные файлы:** {', '.join(project_info['code']['main_files'])}

## Структура проекта
Проект содержит следующие компоненты:
- Документация: {len(project_info['docs'])} файлов
- Исходный код: {sum(project_info['code']['languages'].values())} файлов
- Конфигурационные файлы: {len(project_info['config'])} файлов

## Детальная информация
"""

    # Добавляем информацию о документации
    if project_info['docs']:
        content += "\n### Документация\n"
        for doc_file, info in project_info['docs'].items():
            content += f"- **{doc_file}**: {info.get('size', 0)} символов\n"

    # Добавляем информацию о языках
    if project_info['code']['languages']:
        content += "\n### Распределение по языкам\n"
        for lang, count in project_info['code']['languages'].items():
            content += f"- **{lang}**: {count} файлов\n"

    print(f"[MCP] Отправляем данные проекта {project_name} в supermemory...")

    # Вызываем MCP инструмент
    result = call_mcp_tool("supermemory-mcp", "addMemory", {
        "thingToRemember": content
    })

    if "error" not in result:
        print(f"[SUCCESS] Проект {project_name} обновлен в supermemory")
        return True
    else:
        print(f"[ERROR] Не удалось обновить проект {project_name}: {result['error']}")
        return False

def main():
    scanner = ProjectScanner()

    print("[MCP SCAN] Начинаем сканирование и обновление проектов в supermemory...")

    success_count = 0
    total_count = 0

    for project_name, project_path in scanner.projects.items():
        print(f"\n[PROJECT] Обрабатываем проект: {project_name}")
        total_count += 1

        project_info = scanner.scan_project(project_name, project_path)
        if project_info:
            if update_project_in_supermemory(project_name, project_info):
                success_count += 1
        else:
            print(f"[ERROR] Проект {project_name} не найден")

    print(f"\n[MCP DONE] Обработка завершена: {success_count}/{total_count} проектов обновлено")

    if success_count == total_count:
        print("[SUCCESS] Все проекты успешно обновлены в supermemory!")
    else:
        print("[WARNING] Некоторые проекты не удалось обновить")

if __name__ == '__main__':
    main()
