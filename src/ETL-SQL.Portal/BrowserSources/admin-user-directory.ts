import { isObject } from './http-error.js';

const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === 'string');
const nullableText = (value: unknown) => value == null || typeof value === 'string';
export function isAdminUser(value: unknown): value is UserDto {
  return isObject(value) && typeof value.id === 'number' && Number.isSafeInteger(value.id) && value.id > 0
    && typeof value.username === 'string' && typeof value.isActive === 'boolean' && strings(value.roles) && strings(value.groups)
    && typeof value.mustChangePassword === 'boolean' && typeof value.createdAt === 'string' && Number.isFinite(Date.parse(value.createdAt))
    && typeof value.version === 'number' && Number.isSafeInteger(value.version) && value.version > 0
    && nullableText(value.email) && nullableText(value.firstName) && nullableText(value.lastName) && nullableText(value.provider);
}
function readPage(value: unknown, page: number, pageSize: number): PagedResult<UserDto> {
  if (!isObject(value) || !Array.isArray(value.items) || !value.items.every(isAdminUser)
    || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0
    || value.page !== page || value.pageSize !== pageSize || value.items.length > pageSize) {
    throw new TypeError('Invalid user-directory page response.');
  }
  return { items: value.items, total: value.total, page, pageSize };
}

/** Collect the paginated server directory for callers that need the complete owner list. */
export async function collectAdminUsers(fetchPage: (page: number, pageSize: number) => Promise<unknown>): Promise<UserDto[]> {
  const pageSize = 100, users: UserDto[] = [], ids = new Set<number>();
  let expectedTotal: number | undefined;
  for (let page = 1; ; ++page) {
    const result = readPage(await fetchPage(page, pageSize), page, pageSize);
    expectedTotal ??= result.total;
    if (result.total !== expectedTotal || result.items.length !== Math.min(pageSize, expectedTotal - users.length)) {
      throw new Error('The user directory changed or returned an incomplete page. Refresh and try again.');
    }
    for (const user of result.items) {
      if (ids.has(user.id)) throw new Error('The user directory returned duplicate entries. Refresh and try again.');
      ids.add(user.id); users.push(user);
    }
    if (users.length === expectedTotal) return users;
  }
}
