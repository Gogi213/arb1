# PROPOSAL-2025-0076: Исправление пути сохранения статистики в Analyzer

## Диагностика
Скрипт `analyzer/run_all_ultra.py` в настоящее время сохраняет итоговый `.csv` файл в директорию `summary_stats`, созданную относительно текущей рабочей директории, из которой был запущен скрипт. Это приводит к тому, что файлы статистики могут оказаться в корне проекта или в другом непредсказуемом месте, вместо того чтобы находиться внутри директории `analyzer`.

Проект `charts` ожидает найти эти файлы в `analyzer/summary_stats/`, что вызывает рассогласование.

## Предлагаемое изменение
Предлагается изменить логику сохранения файла в `analyzer/run_all_ultra.py`, чтобы путь к директории `summary_stats` формировался относительно расположения самого скрипта.

### Изменения в `analyzer/run_all_ultra.py`
```python
<<<<<<< SEARCH
:start_line:529
-------
        # Create summary_stats directory inside analyzer if it doesn't exist
        os.makedirs("summary_stats", exist_ok=True)

        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        stats_filename = f"summary_stats/summary_stats_{timestamp}.csv"
        stats_df.write_csv(stats_filename)

        print(f"\n[OK] Summary statistics saved to: {stats_filename}")
=======
        # Create summary_stats directory inside analyzer if it doesn't exist
        analyzer_dir = Path(__file__).parent
        save_dir = analyzer_dir / "summary_stats"
        os.makedirs(save_dir, exist_ok=True)

        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        stats_filename = save_dir / f"summary_stats_{timestamp}.csv"
        stats_df.write_csv(stats_filename)

        print(f"\n[OK] Summary statistics saved to: {stats_filename}")
>>>>>>> REPLACE
```

## Обоснование
Это изменение гарантирует, что файлы статистики всегда будут сохраняться в предсказуемое и правильное место (`analyzer/summary_stats/`), независимо от того, из какой директории был запущен скрипт `run_all_ultra.py`. Это устранит рассогласование с проектом `charts` и сделает поведение системы более надежным.

## Оценка рисков
Риск отсутствует. Изменение является исправлением ошибки и не влияет на логику анализа данных.

## План тестирования
1.  Запустить скрипт `python analyzer/run_all_ultra.py` из корневой директории проекта.
2.  Убедиться, что файл `summary_stats_... .csv` был создан именно в директории `analyzer/summary_stats/`.
3.  Запустить сервер `charts` и убедиться, что он успешно находит и загружает данные из нового файла.

## План отката
1.  Отменить изменения в файле `analyzer/run_all_ultra.py` с помощью `git restore analyzer/run_all_ultra.py`.