import os

# Исключаемые папки: указанные пользователем + типичные билды
excluded_dirs = {'data', 'logs', 'jkorf', 'libs', 'bin', 'obj', 'Debug', 'Release', 'build', 'dist', '__pycache__', '.git', '.vs'}

def write_tree(f, path, prefix=''):
    try:
        # Получаем все элементы, сортируем, пропускаем скрытые
        items = [item for item in os.listdir(path) if not item.startswith('.')]
        dirs = sorted([item for item in items if os.path.isdir(os.path.join(path, item)) and item not in excluded_dirs])
        files = sorted([item for item in items if os.path.isfile(os.path.join(path, item))])

        all_items = dirs + files

        for i, item in enumerate(all_items):
            is_last = (i == len(all_items) - 1)
            connector = '`- ' if is_last else '|- '
            f.write(f"{prefix}{connector}{item}\n")
            if item in dirs:
                extension = '  ' if is_last else '| '
                write_tree(f, os.path.join(path, item), prefix + extension)
    except PermissionError:
        pass

if __name__ == '__main__':
    output_file = os.path.join('docs', 'project_structure.txt')
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write('.\n')
        write_tree(f, '.')
