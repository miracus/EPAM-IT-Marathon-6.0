@L1 @group-pre-order
Feature: Organizer reviews the group pre-order

  The organizer of a group booking shares an invitation, and every friend who joins
  picks dishes for themselves. Before anything goes to the restaurant, the organizer
  sees who has joined, what each of them chose, how much each cart costs and what the
  whole group costs. Guests join and fill their carts through the invitation; the
  scenarios prepare that part directly and check what the organizer is shown.

  Background:
    Given a restaurant with these dishes on its menu
      | dish         | price |
      | Caesar Salad | 12    |
      | Tom Yum      | 20    |
      | Cheesecake   | 8     |
    And the organizer has booked a table for 6 guests

  Rule: The organizer sees every guest with their dishes and what their cart costs

    @api @smoke
    Scenario: Every guest's dishes, quantities and subtotal are listed
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
        | Tom Yum      | 1        |
      And "Ivan" has joined the group and confirmed a cart with
        | dish       | quantity |
        | Cheesecake | 3        |
      When the organizer opens the group pre-order
      Then the organizer sees these guests
        | guest | dishes                      | subtotal | cart      |
        | Anna  | Caesar Salad x2, Tom Yum x1 | 44       | confirmed |
        | Ivan  | Cheesecake x3               | 24       | confirmed |
      And the group total is 68

    @ui @smoke
    Scenario: The group pre-order page lists every guest with their dishes and subtotal
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
        | Tom Yum      | 1        |
      And "Ivan" has joined the group and confirmed a cart with
        | dish       | quantity |
        | Cheesecake | 3        |
      When the organizer opens the group pre-order
      Then the organizer sees these guests
        | guest | dishes                      | subtotal | cart      |
        | Anna  | Caesar Salad x2, Tom Yum x1 | 44       | confirmed |
        | Ivan  | Cheesecake x3               | 24       | confirmed |
      And the group total is 68

  Rule: The group total is the sum of everyone's carts and follows every change

    @api
    Scenario: The total grows when a guest adds a dish
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and is still choosing, with
        | dish         | quantity |
        | Caesar Salad | 1        |
      And the organizer has opened the group pre-order
      When "Anna" adds 2 x "Cheesecake" to their cart
      And the organizer opens the group pre-order
      Then the subtotal of "Anna" is 28
      And the group total is 48
      And the group total equals the sum of everyone's subtotals

    # On the page the grand total is what will be sent: confirmed carts only (see README).
    @ui
    Scenario: The page shows a guest's new dish and the new total after a refresh
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      And the organizer has opened the group pre-order
      When "Anna" adds 2 x "Cheesecake" to their cart
      And the organizer refreshes the group pre-order
      Then the subtotal of "Anna" is 28
      And the group total is 28
      And the group total equals the sum of everyone's subtotals

  Rule: Guests who have not finished are listed too, marked as still choosing

    @api
    Scenario: A guest still choosing and a guest without dishes are listed with their status
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      And "Ivan" has joined the group and is still choosing, with
        | dish       | quantity |
        | Cheesecake | 2        |
      And "Maksym" has joined the group without choosing anything
      When the organizer opens the group pre-order
      Then the organizer sees these guests
        | guest  | dishes          | subtotal | cart           |
        | Anna   | Caesar Salad x1 | 12       | confirmed      |
        | Ivan   | Cheesecake x2   | 16       | still choosing |
        | Maksym |                 | 0        | still choosing |
      And the group total is 28

    @ui
    Scenario: The page marks a guest still choosing and a guest without dishes
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      And "Ivan" has joined the group and is still choosing, with
        | dish       | quantity |
        | Cheesecake | 2        |
      And "Maksym" has joined the group without choosing anything
      When the organizer opens the group pre-order
      Then the organizer sees these guests
        | guest  | dishes          | subtotal | cart           |
        | Anna   | Caesar Salad x1 | 12       | confirmed      |
        | Ivan   | Cheesecake x2   | 16       | still choosing |
        | Maksym |                 | 0        | still choosing |
      And the organizer is warned that 2 carts are not confirmed yet

  Rule: A group nobody has joined yet is empty

    @api
    Scenario: A group without guests has no participants and nothing to pay
      When the organizer opens the group pre-order
      Then nobody is listed in the group
      And the group total is 0

    @ui
    Scenario: The page of a group without guests shows only the organizer's own empty cart
      When the organizer opens the group pre-order
      Then the organizer sees no guests
      And the organizer is told that nobody has chosen any dishes yet
      And the group total is 0

  Rule: Only the organizer of the group can see it

    @api @negative
    Scenario Outline: Someone signed in who is not the organizer is refused
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When <someone> tries to open the group pre-order
      Then they are told that only the organizer can do this

      Examples: Signed-in people who are not the organizer
        | someone          |
        | another customer |
        | the waiter       |

    @api @negative
    Scenario Outline: Someone who is not signed in is asked to sign in
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When <someone> tries to open the group pre-order
      Then they are asked to sign in first

      Examples: People without an account session
        | someone              |
        | a guest of the group |
        | an anonymous visitor |

  Rule: A group that does not exist cannot be opened

    @api @negative
    Scenario: The organizer asks for a group that does not exist
      When the organizer opens a group pre-order that does not exist
      Then the organizer is told "Group reservation was not found"

    @ui @negative
    Scenario: The page of a reservation without a group says it was not found
      When the organizer opens a group pre-order that does not exist
      Then the organizer is told that the group reservation was not found
