import yaml
import re
from tabulate import tabulate
import os

# Function to load the YAML file
def load_yaml(filepath):
    with open(filepath, 'r', encoding='utf-8') as file:
        return yaml.safe_load(file)

# Function to extract PublishPipelineArtifact@1 tasks and their properties
def extract_publish_pipeline_artifacts(data):
    artifacts = []
    
    def extract_tasks(jobs):
        for job in jobs:
            if 'steps' in job:
                for step in job['steps']:
                    if step.get('task') == 'PublishPipelineArtifact@1':
                        artifacts.append(step)
    
    for stage in data.get('stages', []):
        for job in stage.get('jobs', []):
            extract_tasks([job])
    
    return artifacts

# Function to clean displayName
def clean_display_name(display_name):
    if display_name:
        # Remove text within parentheses and the parentheses themselves
        display_name = re.sub(r'\s*\(.*?\)', '', display_name).strip()
        # Remove all non-ASCII characters
        display_name = re.sub(r'[^\x00-\x7F]+', '', display_name).strip()
    return display_name

# Function to normalize condition
def normalize_condition(condition):
    if condition is None or condition == 'succeeded()':
        return 'succeeded()'
    return condition

# Function to normalize paths
def normalize_path(path):
    if path:
        return os.path.normpath(path).replace('\\', '/')
    return path

# Load the YAML files
file1_path = 'c:\\non-1espt.yml'
file2_path = 'c:\\1espt.yml'

data1 = load_yaml(file1_path)
data2 = load_yaml(file2_path)

# Extract the tasks
tasks1 = extract_publish_pipeline_artifacts(data1)
tasks2 = extract_publish_pipeline_artifacts(data2)

# Store the properties in arrays
tasks_array1 = []
for task in tasks1:
    display_name = clean_display_name(task.get('displayName'))
    task_dict = {
        'displayName': display_name,
        'continueOnError': task.get('continueOnError'),
        'condition': normalize_condition(task.get('condition')),
        'artifactName': task.get('inputs', {}).get('artifactName'),
        'targetPath': normalize_path(task.get('inputs', {}).get('targetPath'))
    }
    tasks_array1.append(task_dict)

tasks_array2 = []
for task in tasks2:
    display_name = clean_display_name(task.get('displayName'))
    task_dict = {
        'displayName': display_name,
        'continueOnError': task.get('continueOnError'),
        'condition': normalize_condition(task.get('condition')),
        'artifactName': task.get('inputs', {}).get('artifactName'),
        'targetPath': normalize_path(task.get('inputs', {}).get('targetPath'))
    }
    tasks_array2.append(task_dict)

# Compare the tasks based on displayName, artifactName, and targetPath
common_tasks = []
different_tasks = []
missing_tasks = []

for task1 in tasks_array1:
    found = False
    for task2 in tasks_array2:
        if (task1['displayName'] == task2['displayName'] and
            task1['artifactName'] == task2['artifactName'] and
            task1['targetPath'] == task2['targetPath']):
            found = True
            differences = {}
            for key in task1:
                if key != 'displayName' and task1[key] != task2[key]:
                    differences[key] = (task1[key], task2[key])
            if differences:
                different_tasks.append((task1, task2, differences))
            else:
                common_tasks.append((task1, task2))
            break
    if not found:
        missing_tasks.append(task1)

# Prepare the data for tabulation
table_data = []

# Add common tasks to the table
for task1, task2 in common_tasks:
    table_data.append([task1['displayName'], task2['displayName'], "No differences"])

# Add different tasks to the table
for task1, task2, differences in different_tasks:
    diff_str = "; ".join([f"{key}: {value[0]} != {value[1]}" for key, value in differences.items()])
    table_data.append([task1['displayName'], task2['displayName'], diff_str])

# Add missing tasks to the table
for task in missing_tasks:
    table_data.append([task['displayName'], "Missing", "N/A"])

# Print the table
print(tabulate(table_data, headers=["Task Display Name (non-1espt)", "Task Display Name (1espt)", "Differences"]))
